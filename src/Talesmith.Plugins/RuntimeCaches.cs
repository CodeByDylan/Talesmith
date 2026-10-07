using System.Reflection;
using System.Reflection.Metadata;

namespace Talesmith.Plugins;

/// <summary>Empties the caches the runtime and libraries keep per type, which would otherwise keep unloaded plugin types alive.</summary>
/// <remarks>Uses the cache-clearing hooks libraries provide for hot reload, such as System.Text.Json's serialization metadata caches.</remarks>
internal static class RuntimeCaches
{
    public static void Clear()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsCollectible)
                continue;

            IEnumerable<MetadataUpdateHandlerAttribute> handlers;
            try
            {
                handlers = assembly.GetCustomAttributes<MetadataUpdateHandlerAttribute>();
            }
            catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or FileLoadException or CustomAttributeFormatException)
            {
                continue;
            }

            foreach (var handler in handlers)
            {
                var clear = handler.HandlerType.GetMethod("ClearCache", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, [typeof(Type[])]);
                try
                {
                    clear?.Invoke(null, [null]);
                }
                catch (TargetInvocationException)
                {
                    // A library failing to clear its cache only delays unloading; it is reported as a plugin that stays loaded.
                }
            }
        }
    }
}
