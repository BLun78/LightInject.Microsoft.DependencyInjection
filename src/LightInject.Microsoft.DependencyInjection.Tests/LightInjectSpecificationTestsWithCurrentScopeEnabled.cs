﻿namespace LightInject.Microsoft.DependencyInjection.Tests
{
    using System;
    using global::Microsoft.Extensions.DependencyInjection;
    using global::Microsoft.Extensions.DependencyInjection.Specification;

    public class LightInjectSpecificationTestsWithCurrentScopeEnabled : DependencyInjectionSpecificationTests
    {
        protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            return serviceCollection.CreateLightInjectServiceProvider(new ContainerOptions() { EnableCurrentScope = true });
        }
    }
#if NET8_0_OR_GREATER
    public class KeyedLightInjectSpecificationTestsWithCurrentScopeEnabled : KeyedDependencyInjectionSpecificationTests
    {
        protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            return serviceCollection.CreateLightInjectServiceProvider(new ContainerOptions() { EnableCurrentScope = true });
        }
    }
#endif
}
