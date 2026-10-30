using System.Collections.Immutable;

namespace Macaron.DependencyInjection;

internal sealed class DependencyContainer(
    DependencyContainer? parent,
    ImmutableDictionary<RegistrationKey, TypeRegistration> registry
) : IDependencyContainer
{
    #region Types
    private struct LocalScopeSlot
    {
        internal LocalScope? Scope;
        internal List<CleanupEntry>? Cleanups;

        internal void Deconstruct(out LocalScope? scope, out List<CleanupEntry>? cleanups)
        {
            scope = Scope;
            cleanups = Cleanups;
        }
    }
    #endregion

    #region Static
    private static readonly ImmutableDictionary<RegistrationKey, TypeRegistration> EmptyRegistry =
        ImmutableDictionary.Create<RegistrationKey, TypeRegistration>();

    internal static DependencyContainer Create(
        DependencyContainer? parent,
        ImmutableDictionary<RegistrationKey, TypeRegistration> registry
    )
    {
        var container = new DependencyContainer(parent, registry);

        try
        {
            container.InitializeSharedInstances();

            return container;
        }
        catch (Exception creationError)
        {
            try
            {
                container.Dispose();
            }
            catch (Exception disposalError)
            {
                throw new AggregateException(creationError, disposalError);
            }

            throw new AggregateException(creationError);
        }
    }

    private static void CleanupEntries(List<CleanupEntry> cleanups, ref List<Exception>? exceptions)
    {
        for (var i = cleanups.Count - 1; i >= 0; i--)
        {
            try
            {
                cleanups[i].Invoke();
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        cleanups.Clear();
    }
    #endregion

    #region Fields
    private readonly Dictionary<RegistrationKey, object> _instances = new();
    private readonly List<CleanupEntry> _cleanups = [];
    private readonly Stack<RegistrationKey> _resolutionStack = new();
    private int _creationDepth;
    private bool _isDisposed;
    private bool _isCleaningLocalScope;

    private LocalScopeSlot[] _localScopeSlots = [];
    private int _localScopeSlotCount;
    private int _freeLocalScopeSlotCount;

    private DependencyContainer? _parent = parent;
    private ImmutableDictionary<RegistrationKey, TypeRegistration> _registry = registry;
    #endregion

    #region IDependencyContainer Interface
    public T Resolve<T>(object? tag = null)
    {
        return (T)ResolveRecursively(
            new RegistrationKey(typeof(T), tag),
            scopedInstances: _instances,
            dependencyContainer: this,
            resolver: this
        );
    }

    public bool Contains<T>(object? tag = null)
    {
        ThrowIfDisposed();

        var key = new RegistrationKey(typeof(T), tag);

        return key.IsResolver
            || _registry.ContainsKey(key)
            || (_parent as IDependencyResolver)?.Contains<T>(tag) is true;
    }

    public IDependencyContainer CreateChild()
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateChild));

        return Create(parent: this, registry: EmptyRegistry);
    }

    public IDependencyContainer CreateChild(IDependencyInstaller installer)
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateChild));

        var builder = new DependencyContainerBuilder();

        installer.Install(builder);

        return builder.Build(parent: this);
    }

    public LocalScope CreateLocalScope()
    {
        ThrowIfDisposed();
        ThrowIfLifecycleChangeBlocked(nameof(CreateLocalScope));

        var index = AllocateLocalScopeSlot();
        var localScope = new LocalScope(this, index);

        _localScopeSlots[index].Scope = localScope;

        return localScope;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        ThrowIfLifecycleChangeBlocked(nameof(Dispose));

        // 정리 콜백이 재진입해도 조회와 중복 정리를 막도록 먼저 무효화한다.
        _isDisposed = true;
        _parent = null;
        _registry = _registry.Clear();

        List<Exception>? exceptions = null;

        for (var i = _localScopeSlotCount - 1; i >= 0; i--)
        {
            if (_localScopeSlots[i].Scope is null)
            {
                continue;
            }

            try
            {
                DisposeLocalScopeSlot(i);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        _localScopeSlots = [];
        _localScopeSlotCount = 0;
        _freeLocalScopeSlotCount = 0;

        CleanupEntries(_cleanups, ref exceptions);
        _instances.Clear();

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }
    #endregion

    #region Methods
    private void InitializeSharedInstances()
    {
        ThrowIfDisposed();

        var visitedKeys = new HashSet<RegistrationKey>();

        for (
            var registrationContainer = this;
            registrationContainer is not null;
            registrationContainer = registrationContainer._parent
        )
        {
            registrationContainer.ThrowIfDisposed();

            foreach (var (key, registration) in registrationContainer._registry)
            {
                // 자식의 Transient도 부모 공유 등록을 가리므로 초기화 대상 판정 전에 키를 기록한다.
                if (!visitedKeys.Add(key))
                {
                    continue;
                }

                var isInitializationTarget = registration is TypeRegistration.Scoped
                    || (ReferenceEquals(registrationContainer, this) && registration is TypeRegistration.Singleton);

                if (!isInitializationTarget)
                {
                    continue;
                }

                ResolveRecursively(
                    key,
                    scopedInstances: _instances,
                    dependencyContainer: this,
                    resolver: this
                );
            }
        }
    }

    internal T Resolve<T>(object? tag, LocalScope localScope)
    {
        ThrowIfLocalScopeInvalid(localScope);

        return (T)ResolveRecursively(new RegistrationKey(typeof(T), tag), _instances, dependencyContainer: this, localScope);
    }

    internal bool Contains<T>(object? tag, LocalScope localScope)
    {
        ThrowIfLocalScopeInvalid(localScope);

        return Contains<T>(tag);
    }

    internal void DisposeLocalScope(LocalScope localScope)
    {
        ThrowIfLifecycleChangeBlocked(nameof(Dispose));
        ThrowIfLocalScopeInvalid(localScope);

        DisposeLocalScopeSlot(localScope.Index);
    }

    private object ResolveRecursively(
        RegistrationKey key,
        Dictionary<RegistrationKey, object> scopedInstances,
        DependencyContainer dependencyContainer,
        IDependencyResolver resolver
    )
    {
        ThrowIfDisposed();

        if (key.IsResolver)
        {
            return resolver;
        }

        if (_registry.TryGetValue(key, out var typeRegistration))
        {
            // 등록을 찾은 위치와 요청한 범위를 구분한다. Scoped는 요청 컨테이너의 캐시를,
            // Singleton은 등록 컨테이너의 캐시를 사용하고 Transient는 요청 resolver에 귀속한다.
            return typeRegistration switch
            {
                TypeRegistration.Transient { Factory: var factory, Cleanup: var cleanup } =>
                    dependencyContainer.CreateInstance(
                        key,
                        factory,
                        cleanup,
                        resolver,
                        dependencyContainer.GetTransientCleanups(resolver)
                    ),
                TypeRegistration.Scoped { Factory: var factory, Cleanup: var cleanup } =>
                    dependencyContainer.GetOrAdd(
                        scopedInstances,
                        key,
                        factory,
                        cleanup
                    ),
                TypeRegistration.Singleton { Factory: var factory, Cleanup: var cleanup } =>
                    GetOrAdd(
                        _instances,
                        key,
                        factory,
                        cleanup
                    ),
                _ => throw new InvalidOperationException($"Not supported type registration: {typeRegistration}")
            };
        }

        if (_parent == null)
        {
            throw new InvalidOperationException($"Service not found: {key}");
        }

        return _parent.ResolveRecursively(key, scopedInstances, dependencyContainer, resolver);
    }

    private object GetOrAdd(
        Dictionary<RegistrationKey, object> instances,
        RegistrationKey key,
        DependencyFactory<object> factory,
        Action<object>? cleanup
    )
    {
        if (instances.TryGetValue(key, out var instance))
        {
            return instance;
        }

        // 공유 서비스의 의존성은 최초 조회 범위가 아니라 그 서비스를 소유한 컨테이너에 귀속한다.
        var newInstance = CreateInstance(key, factory, cleanup, resolver: this, cleanups: _cleanups);

        instances.Add(key, newInstance);

        return newInstance;
    }

    private object CreateInstance(
        RegistrationKey key,
        DependencyFactory<object> factory,
        Action<object>? cleanup,
        IDependencyResolver resolver,
        List<CleanupEntry> cleanups
    )
    {
        // 실제로 서비스를 생성하는 컨테이너에서 키를 추적해 서로 다른 Scoped 캐시를 구분한다.
        foreach (var resolvingKey in _resolutionStack)
        {
            if (resolvingKey == key)
            {
                var dependencyPath = string.Join(" -> ", _resolutionStack.Reverse().Append(key));

                throw new InvalidOperationException($"Cyclic dependency detected: {dependencyPath}");
            }
        }

        var cleanupCollector = new CleanupCollector();
        object instance;

        // 실패한 수집 항목의 정리가 끝날 때까지 순환 추적과 범위 생성·해제 제한을 유지한다.
        _resolutionStack.Push(key);
        _creationDepth += 1;

        try
        {
            instance = factory.Invoke(resolver, key, ref cleanupCollector);

            ThrowIfDisposed();
        }
        catch (Exception exception)
        {
            // 이 팩토리의 수집 항목만 정리하고 이미 Resolve한 의존성은 기존 귀속 범위에 남긴다.
            CleanupFailedFactory(in cleanupCollector, exception);

            throw;
        }
        finally
        {
            _creationDepth -= 1;
            _resolutionStack.Pop();
        }

        // 역순 정리에서 수집 항목이 서비스보다 먼저 실행되도록 서비스를 먼저 기록한다.
        cleanups.Add(new CleanupEntry(instance, cleanup));

        foreach (var entry in cleanupCollector.Entries)
        {
            cleanups.Add(entry);
        }

        return instance;

        #region Local Functions
        static void CleanupFailedFactory(in CleanupCollector cleanupCollector, Exception factoryException)
        {
            var exceptions = new List<Exception> { factoryException };
            var entries = cleanupCollector.Entries;

            for (var i = entries.Length - 1; i >= 0; i--)
            {
                try
                {
                    entries[i].Invoke();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }
            }

            if (exceptions.Count > 1)
            {
                throw new AggregateException(exceptions);
            }
        }
        #endregion
    }

    private int AllocateLocalScopeSlot()
    {
        if (_freeLocalScopeSlotCount > 0)
        {
            for (var i = 0; i < _localScopeSlotCount; i++)
            {
                if (_localScopeSlots[i].Scope is null)
                {
                    _freeLocalScopeSlotCount -= 1;

                    return i;
                }
            }
        }

        if (_localScopeSlotCount == _localScopeSlots.Length)
        {
            var capacity = _localScopeSlots.Length == 0 ? 4 : _localScopeSlots.Length * 2;

            Array.Resize(ref _localScopeSlots, capacity);
        }

        var newIndex = _localScopeSlotCount;

        _localScopeSlotCount += 1;

        return newIndex;
    }

    private List<CleanupEntry> GetTransientCleanups(IDependencyResolver resolver)
    {
        if (resolver is not LocalScope localScope)
        {
            return _cleanups;
        }

        ref var slot = ref GetLocalScopeSlot(localScope);

        return slot.Cleanups ??= [];
    }

    private ref LocalScopeSlot GetLocalScopeSlot(LocalScope localScope)
    {
        ThrowIfLocalScopeInvalid(localScope);

        return ref _localScopeSlots[localScope.Index];
    }

    private void DisposeLocalScopeSlot(int index)
    {
        var (scope, cleanups) = _localScopeSlots[index];

        // 정리 콜백 전에 슬롯을 비우고 범위의 컨테이너 참조를 끊어 이전 참조를 무효화한다.
        _localScopeSlots[index] = default;
        _freeLocalScopeSlotCount += 1;

        scope!.Invalidate();

        _isCleaningLocalScope = true;

        List<Exception>? exceptions = null;

        try
        {
            if (cleanups is not null)
            {
                CleanupEntries(cleanups, ref exceptions);
            }
        }
        finally
        {
            _isCleaningLocalScope = false;
        }

        if (exceptions is not null)
        {
            throw new AggregateException(exceptions);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(objectName: nameof(DependencyContainer));
        }
    }

    private void ThrowIfLifecycleChangeBlocked(string operation)
    {
        if (_creationDepth > 0)
        {
            throw new InvalidOperationException($"Cannot call {operation} while a service is being created.");
        }

        if (_isCleaningLocalScope)
        {
            throw new InvalidOperationException($"Cannot call {operation} while a local scope is being disposed.");
        }
    }

    private void ThrowIfLocalScopeInvalid(LocalScope localScope)
    {
        ThrowIfDisposed();

        var index = localScope.Index;

        // 슬롯 인덱스는 재사용되므로 현재 슬롯을 소유한 범위 객체의 참조까지 확인한다.
        if ((uint)index >= (uint)_localScopeSlotCount || !ReferenceEquals(_localScopeSlots[index].Scope, localScope))
        {
            throw new ObjectDisposedException(nameof(LocalScope));
        }
    }
    #endregion
}

