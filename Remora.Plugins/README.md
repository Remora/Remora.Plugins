Remora.Plugins
==============

Remora.Plugins is a simple plugin system for .NET, providing a dynamic pluggable
framework for your applications. In short, class libraries can be written as 
standalone packages and loaded at runtime as integrated parts of your 
application, allowing loose coupling and easily swappable components. The plugin 
system is designed around Microsoft's dependency injection framework.

## Usage
Creating a plugin is as simple as creating a class library project, and 
annotating the assembly with an attribute denoting the type used as a plugin 
descriptor. The descriptor acts as the entry point of the plugin, as well as an
encapsulation point for information about it.

Generally, plugins should only reference Remora.Plugins.Abstractions, while the
main application should reference and use Remora.Plugins.

```c#
[assembly: RemoraPlugin]

public sealed class MyPlugin(MyService myService) : IPluginDescriptor
{
    /// <inheritdoc />
    public string Name => "My Plugin";

    /// <inheritdoc />
    public string Description => "My plugin that does a thing.";

    /// <inheritdoc/>
    public static IServiceCollection ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<MyService>();

        return serviceCollection;
    }

    /// <inheritdoc />
    public override async ValueTask<Result> InitializeAsync(CancellationToken ct = default)
    {
        var doThing = await myService.DoTheThingAsync(ct);
        if (!doThing.IsSuccess)
        {
            return doThing;
        }

        return Result.FromSuccess();
    }
}
```

Loading plugins in your application is equally simple. The example below is
perhaps a little convoluted, but shows the flexibility of the system.

```c#
var pluginServiceOptions = new(["./plugins", "./mods"], scanAssemblyDirectory: false);
var pluginService = new PluginService(pluginServiceOptions);

var serviceCollection = new ServiceCollection();

// Load plugins where the plugin name is greater than 3 characters long.
var pluginTreeBuilder = pluginService.LoadPluginTree(serviceCollection, filter: plugin => plugin.Name.Length > 3);

_services = serviceCollection.BuildServiceProvider();
var pluginTree = pluginTreeBuilder.Build(_services);

var initializePlugins = await pluginTree.InitializeAsync(ct);
if (!initializePlugins.IsSuccess)
{
    // check initializePlugins.Error to figure out why
    return;
}

var migratePlugins = await pluginTree.MigrateAsync(ct);
if (!migratePlugins.IsSuccess)
{
    // check migratePlugins.Error to figure out why
    return;
}

```

Plugins should be designed in such a way that a registration or initialization 
failure does not corrupt the application.

## Building
The library does not require anything out of the ordinary to compile.

```bash
cd $SOLUTION_DIR
dotnet build
dotnet pack -c Release
```

## Downloading
Get it on [NuGet][1].


[1]: https://www.nuget.org/packages/Remora.Plugins/
