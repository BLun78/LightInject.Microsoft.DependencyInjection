/*********************************************************************************
    The MIT License (MIT)

    Copyright (c) 2022 bernhard.richter@gmail.com

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
******************************************************************************
    http://www.lightinject.net/
    http://twitter.com/bernhardrichter
******************************************************************************/
#if NET6_0_OR_GREATER
#define USE_ASYNCDISPOSABLE
#endif
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1126:PrefixCallsCorrectly", Justification = "Reviewed")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1101:PrefixLocalCallsWithThis", Justification = "No inheritance")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleClass", Justification = "Single source file deployment.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1633:FileMustHaveHeader", Justification = "Custom header.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:ElementsMustBeDocumented", Justification = "All public members are documented.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "Performance")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("MaintainabilityRules", "SA1403", Justification = "One source file")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("DocumentationRules", "SA1649", Justification = "One source file")]

namespace LightInject.Microsoft.DependencyInjection;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using global::Microsoft.Extensions.DependencyInjection;
using LightInject;

/// <summary>
/// Extends the <see cref="IServiceCollection"/> interface.
/// </summary>
public static class LightInjectServiceCollectionExtensions
{
    /// <summary>
    /// Create a new <see cref="IServiceProvider"/> from the given <paramref name="serviceCollection"/>.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> from which to create an <see cref="IServiceProvider"/>.</param>
    /// <returns>An <see cref="IServiceProvider"/> that is backed by an <see cref="IServiceContainer"/>.</returns>
    public static IServiceProvider CreateLightInjectServiceProvider(this IServiceCollection serviceCollection)
        => serviceCollection.CreateLightInjectServiceProvider(ContainerOptions.Default);

    /// <summary>
    /// Create a new <see cref="IServiceProvider"/> from the given <paramref name="serviceCollection"/>.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> from which to create an <see cref="IServiceProvider"/>.</param>
    /// <param name="options">The <see cref="ContainerOptions"/> to be used when creating the <see cref="ServiceContainer"/>.</param>
    /// <returns>An <see cref="IServiceProvider"/> that is backed by an <see cref="IServiceContainer"/>.</returns>
    public static IServiceProvider CreateLightInjectServiceProvider(this IServiceCollection serviceCollection, ContainerOptions options)
    {
        var clonedOptions = options.Clone();
        clonedOptions.WithMicrosoftSettings();
        var container = new ServiceContainer(clonedOptions);
        return container.CreateServiceProvider(serviceCollection);
    }

    /// <summary>
    /// Create a new <see cref="IServiceProvider"/> from the given <paramref name="serviceCollection"/>.
    /// </summary>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> from which to create an <see cref="IServiceProvider"/>.</param>
    /// <param name="configureOptions">A delegate used to configure <see cref="ContainerOptions"/>.</param>
    /// <returns>An <see cref="IServiceProvider"/> that is backed by an <see cref="IServiceContainer"/>.</returns>
    public static IServiceProvider CreateLightInjectServiceProvider(this IServiceCollection serviceCollection, Action<ContainerOptions> configureOptions)
    {
        var options = ContainerOptions.Default.Clone().WithMicrosoftSettings();
        configureOptions(options);
        return CreateLightInjectServiceProvider(serviceCollection, options);
    }
}

/// <summary>
/// Extends the <see cref="IServiceContainer"/> interface.
/// </summary>
public static class DependencyInjectionContainerExtensions
{
    /// <summary>
    /// Creates an <see cref="IServiceProvider"/> based on the given <paramref name="serviceCollection"/>.
    /// </summary>
    /// <param name="container">The target <see cref="IServiceContainer"/>.</param>
    /// <param name="serviceCollection">The <see cref="IServiceCollection"/> that contains information about the services to be registered.</param>
    /// <returns>A configured <see cref="IServiceProvider"/>.</returns>
    public static IServiceProvider CreateServiceProvider(this IServiceContainer container, IServiceCollection serviceCollection)
    {
        if (container.AvailableServices.Any(sr => sr.ServiceType == typeof(IServiceProvider)))
        {
            throw new InvalidOperationException("CreateServiceProvider can only be called once per IServiceContainer instance.");
        }

        var rootScope = container.BeginScope();
        rootScope.Completed += (a, s) => container.Dispose();
        container.Register<IServiceProvider>(f => new LightInjectServiceProvider((Scope)f));
        container.RegisterSingleton<IServiceScopeFactory>(f => new LightInjectServiceScopeFactory(container));
        container.RegisterSingleton<IServiceProviderIsService>(factory => new LightInjectIsServiceProviderIsService(serviceType => container.CanGetInstance(serviceType, string.Empty)));
        RegisterServices(container, rootScope, serviceCollection);
        return new LightInjectServiceScope(rootScope).ServiceProvider;
    }

    private static void RegisterServices(IServiceContainer container, Scope rootScope, IServiceCollection serviceCollection)
    {
        var registrations = serviceCollection.Select(d => CreateServiceRegistration(d, rootScope)).ToList();

        var servicesThatRequireNamePrefix = container.AvailableServices
            .GroupBy(si => si.ServiceType)
            .Select(g => new { ServiceType = g.Key, Prefix = g.OrderBy(g => g.ServiceName).Last().ServiceName })
            .ToDictionary(g => g.ServiceType, g => g.Prefix);



        for (int i = 0; i < registrations.Count; i++)
        {
            ServiceRegistration registration = registrations[i];
#if NET8_0_OR_GREATER
            if (registration is KeyedServiceRegistration { IsKeyedService: false } serviceRegistration)
            {
                if (servicesThatRequireNamePrefix.TryGetValue(serviceRegistration.ServiceType, out string prefix))
                {
                    serviceRegistration.ServiceName = prefix + i.ToString("D8", CultureInfo.InvariantCulture.NumberFormat);
                }
                else
                {
                    serviceRegistration.ServiceName = i.ToString("D8", CultureInfo.InvariantCulture.NumberFormat);
                }
            }
#else
            if (servicesThatRequireNamePrefix.TryGetValue(registration.ServiceType, out string prefix))
            {
                registration.ServiceName = prefix + i.ToString("D8", CultureInfo.InvariantCulture.NumberFormat);
            }
            else
            {
                registration.ServiceName = i.ToString("D8", CultureInfo.InvariantCulture.NumberFormat);
            }
#endif
            container.Register(registration);
        }
    }

    private static ServiceRegistration CreateServiceRegistration(ServiceDescriptor serviceDescriptor, Scope rootScope)
    {
#if NET8_0_OR_GREATER
        if (serviceDescriptor.IsKeyedService && serviceDescriptor.KeyedImplementationFactory != null)
        {
            return CreateServiceRegistrationForFactoryDelegate(serviceDescriptor, rootScope);
        }
        else if (!serviceDescriptor.IsKeyedService && serviceDescriptor.ImplementationFactory != null)
        {
            return CreateServiceRegistrationForFactoryDelegate(serviceDescriptor, rootScope);
        }

        if (serviceDescriptor.IsKeyedService && serviceDescriptor.KeyedImplementationInstance != null)
        {
            return CreateServiceRegistrationForInstance(serviceDescriptor, rootScope);
        }
        else if (!serviceDescriptor.IsKeyedService && serviceDescriptor.ImplementationInstance != null)
        {
            return CreateServiceRegistrationForInstance(serviceDescriptor, rootScope);
        }

        return CreateServiceRegistrationServiceType(serviceDescriptor, rootScope);
#else
        if (serviceDescriptor.ImplementationFactory != null)
        {
            return CreateServiceRegistrationForFactoryDelegate(serviceDescriptor, rootScope);
        }

        if (serviceDescriptor.ImplementationInstance != null)
        {
            return CreateServiceRegistrationForInstance(serviceDescriptor, rootScope);
        }

        return CreateServiceRegistrationServiceType(serviceDescriptor, rootScope);
#endif
    }

    private static ServiceRegistration CreateServiceRegistrationForFactoryDelegate(ServiceDescriptor serviceDescriptor, Scope rootScope)
    {
        ServiceRegistration registration = CreateBasicServiceRegistration(serviceDescriptor, rootScope);
        registration.FactoryExpression = CreateFactoryDelegate(serviceDescriptor);
        return registration;
    }

    private static ServiceRegistration CreateServiceRegistrationForInstance(ServiceDescriptor serviceDescriptor, Scope rootScope)
    {
        ServiceRegistration registration = CreateBasicServiceRegistration(serviceDescriptor, rootScope);
#if NET8_0_OR_GREATER
        if (serviceDescriptor.IsKeyedService)
        {
            registration.Value = serviceDescriptor.KeyedImplementationInstance;
        }
        else
        {
            registration.Value = serviceDescriptor.ImplementationInstance;
        }
#else
        registration.Value = serviceDescriptor.ImplementationInstance;
#endif
        return registration;
    }


    private static ServiceRegistration CreateServiceRegistrationServiceType(ServiceDescriptor serviceDescriptor, Scope rootScope)
    {
        ServiceRegistration registration = CreateBasicServiceRegistration(serviceDescriptor, rootScope);
#if NET8_0_OR_GREATER
        if (serviceDescriptor.IsKeyedService)
        {
            registration.ImplementingType = serviceDescriptor.KeyedImplementationType;
        }
        else
        {
            registration.ImplementingType = serviceDescriptor.ImplementationType;
        }
#else
        registration.ImplementingType = serviceDescriptor.ImplementationType;
#endif
        return registration;
    }

    private static ServiceRegistration CreateBasicServiceRegistration(ServiceDescriptor serviceDescriptor, Scope rootScope)
    {
        ServiceRegistration registration = default;
#if NET8_0_OR_GREATER
        registration = new KeyedServiceRegistration
        {
            Lifetime = ResolveLifetime(serviceDescriptor, rootScope),
            ServiceType = serviceDescriptor.ServiceType,
            IsKeyedService = serviceDescriptor.IsKeyedService,
        };
        if (serviceDescriptor.IsKeyedService)
        {
            if (serviceDescriptor.ServiceKey != null &&
                !string.IsNullOrWhiteSpace(serviceDescriptor.ServiceKey.ToString()))
            {
                registration.ServiceName = serviceDescriptor.ServiceKey?.ToString();
            }
            else
            {
                registration.ServiceName = Guid.NewGuid().ToString();
            }
        }
        else
        {
            registration.ServiceName = Guid.NewGuid().ToString();
        }
#else
        registration = new ServiceRegistration
        {
            Lifetime = ResolveLifetime(serviceDescriptor, rootScope),
            ServiceType = serviceDescriptor.ServiceType,
            ServiceName = Guid.NewGuid().ToString(),
        };
#endif

        return registration;
    }

    private static ILifetime ResolveLifetime(ServiceDescriptor serviceDescriptor, Scope rootScope)
    {
        ILifetime lifetime = null;

        switch (serviceDescriptor.Lifetime)
        {
            case ServiceLifetime.Scoped:
                lifetime = new PerScopeLifetime();
                break;
            case ServiceLifetime.Singleton:
                lifetime = new PerRootScopeLifetime(rootScope);
                break;
            case ServiceLifetime.Transient:
                lifetime = NeedsTracking(serviceDescriptor) ? new PerRequestLifeTime() : null;
                break;
        }

        return lifetime;
    }

    private static bool NeedsTracking(ServiceDescriptor serviceDescriptor)
    {
        if (typeof(IDisposable).IsAssignableFrom(serviceDescriptor.ServiceType))
        {
            return true;
        }
#if NET8_0_OR_GREATER
        if (serviceDescriptor.IsKeyedService
            && serviceDescriptor.KeyedImplementationType != null
            && !typeof(IDisposable).IsAssignableFrom(serviceDescriptor.KeyedImplementationType))
        {
            return false;
        }

        if (!serviceDescriptor.IsKeyedService
            && serviceDescriptor.ImplementationType != null
            && !typeof(IDisposable).IsAssignableFrom(serviceDescriptor.ImplementationType))
        {
            return false;
        }
#else
        if (serviceDescriptor.ImplementationType != null
            && !typeof(IDisposable).IsAssignableFrom(serviceDescriptor.ImplementationType))
        {
            return false;
        }
#endif

        return true;
    }

    private static Delegate CreateFactoryDelegate(ServiceDescriptor serviceDescriptor)
    {
        var openGenericMethod = typeof(DependencyInjectionContainerExtensions).GetTypeInfo().GetDeclaredMethod("CreateTypedFactoryDelegate");
        var closedGenericMethod = openGenericMethod.MakeGenericMethod(serviceDescriptor.ServiceType.UnderlyingSystemType);
        return (Delegate)closedGenericMethod.Invoke(null, new object[] { serviceDescriptor });
    }

#pragma warning disable IDE0051
    private static Func<IServiceFactory, T> CreateTypedFactoryDelegate<T>(ServiceDescriptor serviceDescriptor)
#if NET8_0_OR_GREATER
        => serviceFactory =>
        {
            if (serviceDescriptor.IsKeyedService)
            {
                return (T)serviceDescriptor.KeyedImplementationFactory(
                    new LightInjectServiceProvider((Scope)serviceFactory), serviceDescriptor.ServiceKey);
            }
            else
            {
                return (T)serviceDescriptor.ImplementationFactory(new LightInjectServiceProvider((Scope)serviceFactory));
            }
        };
#else
 => serviceFactory => (T)serviceDescriptor.ImplementationFactory(new LightInjectServiceProvider((Scope)serviceFactory));
#endif

#pragma warning restore IDE0051
}

/// <summary>
/// Extends the <see cref="ContainerOptions"/> class.
/// </summary>
public static class ContainerOptionsExtensions
{
    /// <summary>
    /// Sets up the <see cref="ContainerOptions"/> to be compliant with the conventions used in Microsoft.Extensions.DependencyInjection.
    /// </summary>
    /// <param name="options">The target <see cref="ContainerOptions"/>.</param>
    /// <returns><see cref="ContainerOptions"/>.</returns>
    public static ContainerOptions WithMicrosoftSettings(this ContainerOptions options)
    {
        //options.DefaultServiceSelector = serviceNames => serviceNames.SingleOrDefault(string.IsNullOrWhiteSpace) ?? serviceNames.Last();
        options.DefaultServiceSelector = serviceNames => serviceNames.Last();
        options.EnablePropertyInjection = false;
        options.EnableCurrentScope = false;
        options.EnableOptionalArguments = true;
        return options;
    }

    /// <summary>
    /// Creates a clone of the given paramref name="containerOptions".
    /// </summary>
    /// <param name="containerOptions">The <see cref="ContainerOptions"/> for which to create a clone.</param>
    /// <returns>A clone of the given paramref name="containerOptions".</returns>
    public static ContainerOptions Clone(this ContainerOptions containerOptions) => new ContainerOptions()
    {
        DefaultServiceSelector = containerOptions.DefaultServiceSelector,
        EnableCurrentScope = containerOptions.EnableCurrentScope,
        EnablePropertyInjection = containerOptions.EnablePropertyInjection,
        EnableVariance = containerOptions.EnableVariance,
        LogFactory = containerOptions.LogFactory,
        VarianceFilter = containerOptions.VarianceFilter,
        EnableOptionalArguments = containerOptions.EnableOptionalArguments,
    };
}

/// <summary>
/// Creates a LightInject container builder.
/// </summary>
public class LightInjectServiceProviderFactory : IServiceProviderFactory<IServiceContainer>
{
    private readonly Func<IServiceContainer> containerFactory;

    private IServiceCollection services;

    /// <summary>
    /// Initializes a new instance of the <see cref="LightInjectServiceProviderFactory"/> class.
    /// </summary>
    public LightInjectServiceProviderFactory()
        : this(ContainerOptions.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LightInjectServiceProviderFactory"/> class.
    /// </summary>
    /// <param name="options">The <see cref="ContainerOptions"/> to be used when creating the <see cref="ServiceContainer"/>.</param>
    public LightInjectServiceProviderFactory(ContainerOptions options)
    {
        var clonedOptions = options.Clone();
        clonedOptions.WithMicrosoftSettings();
        containerFactory = () => new ServiceContainer(clonedOptions);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LightInjectServiceProviderFactory"/> class.
    /// </summary>
    /// <param name="configureOptions">A delegate used to configure <see cref="ContainerOptions"/>.</param>
    public LightInjectServiceProviderFactory(Action<ContainerOptions> configureOptions)
    {
        var options = ContainerOptions.Default.Clone().WithMicrosoftSettings();
        configureOptions(options);
        containerFactory = () => new ServiceContainer(options);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LightInjectServiceProviderFactory"/> class.
    /// </summary>
    /// <param name="serviceContainer">The <see cref="IServiceContainer"/> to be used.</param>
    public LightInjectServiceProviderFactory(IServiceContainer serviceContainer)
        => containerFactory = () => serviceContainer;

    /// <inheritdoc/>
    public IServiceContainer CreateBuilder(IServiceCollection services)
    {
        this.services = services;
        return containerFactory();
    }

    /// <inheritdoc/>
    public IServiceProvider CreateServiceProvider(IServiceContainer containerBuilder)
        => containerBuilder.CreateServiceProvider(services);
}

/// <summary>
/// An <see cref="IServiceProvider"/> that uses LightInject as the underlying container.
/// </summary>
#if NET8_0_OR_GREATER
internal class LightInjectServiceProvider : IKeyedServiceProvider, IServiceProvider, ISupportRequiredService, IDisposable, IAsyncDisposable
#elif USE_ASYNCDISPOSABLE
internal class LightInjectServiceProvider : IServiceProvider, ISupportRequiredService, IDisposable, IAsyncDisposable
#else
internal class LightInjectServiceProvider : IServiceProvider, ISupportRequiredService, IDisposable
#endif
{
    private readonly Scope scope;

    private bool isDisposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="LightInjectServiceProvider"/> class.
    /// </summary>
    /// <param name="scope">The <see cref="Scope"/> from which this service provider requests services.</param>
    public LightInjectServiceProvider(Scope scope)
        => this.scope = scope;

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        scope.Dispose();
    }
#if USE_ASYNCDISPOSABLE

    public ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return ValueTask.CompletedTask;
        }

        isDisposed = true;

        return scope.DisposeAsync();
    }
#endif

    /// <summary>
    /// Gets an instance of the given <paramref name="serviceType"/>.
    /// </summary>
    /// <param name="serviceType">The service type to return.</param>
    /// <returns>An instance of the given <paramref name="serviceType"/>.
    /// Throws an exception if it cannot be created.</returns>
    public object GetRequiredService(Type serviceType)
        => scope.GetInstance(serviceType);

    /// <summary>
    /// Gets an instance of the given <paramref name="serviceType"/>.
    /// </summary>
    /// <param name="serviceType">The service type to return.</param>
    /// <returns>An instance of the given <paramref name="serviceType"/>.</returns>
    public object GetService(Type serviceType)
        => scope.TryGetInstance(serviceType);
#if NET8_0_OR_GREATER
    /// <summary>
    /// Gets an instance of the given <paramref name="serviceType"/>.
    /// </summary>
    /// <param name="serviceType">The service type to return.</param>
    /// <returns>An instance of the given <paramref name="serviceType"/>.
    /// Throws an exception if it cannot be created.</returns>
    public object GetKeyedService(Type serviceType, object serviceKey)
        => scope.TryGetInstance(serviceType, serviceKey?.ToString());

    /// <summary>
    /// Gets an instance of the given <paramref name="serviceType"/>.
    /// </summary>
    /// <param name="serviceType">The service type to return.</param>
    /// <returns>An instance of the given <paramref name="serviceType"/>.</returns>
    public object GetRequiredKeyedService(Type serviceType, object serviceKey)
        => scope.GetInstance(serviceType, serviceKey?.ToString());
#endif
}

/// <summary>
/// An <see cref="IServiceScopeFactory"/> that uses an <see cref="IServiceContainer"/> to create new scopes.
/// </summary>
internal class LightInjectServiceScopeFactory : IServiceScopeFactory
{
    private readonly IServiceContainer container;

    /// <summary>
    /// Initializes a new instance of the <see cref="LightInjectServiceScopeFactory"/> class.
    /// </summary>
    /// <param name="container">The <see cref="IServiceContainer"/> used to create new scopes.</param>
    public LightInjectServiceScopeFactory(IServiceContainer container)
        => this.container = container;

    /// <inheritdoc/>
    public IServiceScope CreateScope()
        => new LightInjectServiceScope(container.BeginScope());
}

/// <summary>
/// An <see cref="IServiceScope"/> implementation that wraps a <see cref="Scope"/>.
/// </summary>
#if USE_ASYNCDISPOSABLE
internal class LightInjectServiceScope : IServiceScope, IAsyncDisposable
#else
internal class LightInjectServiceScope : IServiceScope
#endif
{
    private readonly Scope wrappedScope;

    /// <summary>
    /// Initializes a new instance of the <see cref="LightInjectServiceScope"/> class.
    /// </summary>
    /// <param name="scope">The <see cref="Scope"/> wrapped by this class.</param>
    public LightInjectServiceScope(Scope scope)
    {
        wrappedScope = scope;
        ServiceProvider = new LightInjectServiceProvider(scope);
    }

    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc/>
    public void Dispose() => wrappedScope.Dispose();

#if USE_ASYNCDISPOSABLE
    /// <inheritdoc/>
    public ValueTask DisposeAsync() => wrappedScope.DisposeAsync();
#endif
}

/// <summary>
/// An <see cref="ILifetime"/> implementation that makes it possible to mimic the notion of a root scope.
/// </summary>
[LifeSpan(30)]
internal class PerRootScopeLifetime : ILifetime, ICloneableLifeTime
{
    private readonly object syncRoot = new object();
    private readonly Scope rootScope;
    private object instance;

    /// <summary>
    /// Initializes a new instance of the <see cref="PerRootScopeLifetime"/> class.
    /// </summary>
    /// <param name="rootScope">The root <see cref="Scope"/>.</param>
    public PerRootScopeLifetime(Scope rootScope)
        => this.rootScope = rootScope;

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public object GetInstance(Func<object> createInstance, Scope scope)
        => throw new NotImplementedException("Uses optimized non closing method");

    /// <inheritdoc/>
    public ILifetime Clone()
        => new PerRootScopeLifetime(rootScope);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable IDE0060
    public object GetInstance(GetInstanceDelegate createInstance, Scope scope, object[] arguments)
    {
#pragma warning restore IDE0060
        if (instance != null)
        {
            return instance;
        }

        lock (syncRoot)
        {
            if (instance == null)
            {
                instance = createInstance(arguments, rootScope);
                RegisterForDisposal(instance);
            }
        }

        return instance;
    }

    private void RegisterForDisposal(object instance)
    {
        if (instance is IDisposable disposable)
        {
            rootScope.TrackInstance(disposable);
        }
        else if (instance is IAsyncDisposable asyncDisposable)
        {
            rootScope.TrackInstance(asyncDisposable);
        }
    }
}

internal class LightInjectIsServiceProviderIsService : IServiceProviderIsService
{
    private readonly Func<Type, bool> canGetService;

    public LightInjectIsServiceProviderIsService(Func<Type, bool> canGetService)
        => this.canGetService = canGetService;

    public bool IsService(Type serviceType)
    {
        if (serviceType.IsGenericTypeDefinition)
        {
            return false;
        }

        return canGetService(serviceType);
    }
}
#if NET8_0_OR_GREATER
/// <summary>
/// Contains information about a registered service.
/// </summary>
internal class KeyedServiceRegistration : ServiceRegistration
{
    /// <summary>
    /// Indicates whether the service is a keyed service.
    /// </summary>
    public bool IsKeyedService { get; set; }

    /// <summary>
    /// Returns a string representation of the <see cref="ServiceRegistration"/>.
    /// </summary>
    /// <returns>A string representation of the <see cref="ServiceRegistration"/>.</returns>
    public override string ToString()
    {
        var lifeTime = Lifetime?.ToString() ?? "Transient";
        return $"ServiceType: '{ServiceType}', ServiceName: '{ServiceName}', ImplementingType: '{ImplementingType}', Lifetime: '{lifeTime}', IsKeyedService: '{IsKeyedService}'";
    }
}
#endif
