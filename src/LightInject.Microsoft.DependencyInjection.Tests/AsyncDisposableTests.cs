using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LightInject.Microsoft.DependencyInjection.Tests
{
    public class AsyncDisposableTests
    {
        [Fact]
        public async Task ShouldDisposeAsyncDisposable()
        {
            var serviceCollection = new ServiceCollection();
            List<object> disposedObjects = new();
            serviceCollection.AddScoped<AsyncDisposable>(sp => new AsyncDisposable(disposedObject => disposedObjects.Add(disposedObject)));

            var serviceProvider = serviceCollection.CreateLightInjectServiceProvider();

            AsyncDisposable asyncDisposable = null;
            await using (var scope = serviceProvider.CreateAsyncScope())
            {
                asyncDisposable = scope.ServiceProvider.GetService<AsyncDisposable>();
            }

            Assert.Contains(asyncDisposable, disposedObjects);
        }
    }

    public class AsyncDisposable : IAsyncDisposable
    {
        private readonly Action<object> onDisposed;

        public AsyncDisposable(Action<object> onDisposed)
        {
            this.onDisposed = onDisposed;
        }
        public ValueTask DisposeAsync()
        {
            onDisposed(this);
            return ValueTask.CompletedTask;
        }
    }

    public class Disposable : IDisposable
    {
        private readonly Action<object> onDisposed;

        public Disposable(Action<object> onDisposed)
        {
            this.onDisposed = onDisposed;
        }

        public void Dispose()
        {
            onDisposed(this);
        }
    }
}