using Talesmith.Authoring;
using Talesmith.Editor.Inspector;
using Talesmith.Lighting;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;
using Talesmith.Scripting;

namespace Talesmith.Editor.Tests.Inspector;

public sealed class ComponentDocumentationTests
{
    private static readonly ValueConverterRegistry Converters = ValueConverterRegistry.CreateDefault();

    [Fact]
    public void BuiltInComponentsOpenTheirSectionOfTheComponentsGuide()
    {
        Assert.Equal("https://talesmith.dev/guide/scenes/components#light-2d", Documentation<Light2D>());
        Assert.Equal("https://talesmith.dev/guide/scenes/components#sprite-animator", Documentation<SpriteAnimator>());
        Assert.Equal("https://talesmith.dev/guide/scenes/components#scripts", Documentation<ScriptComponent>());
    }

    [Fact]
    public void OtherComponentsOpenTheSectionAboutPluginAndScriptComponents() =>
        Assert.Equal("https://talesmith.dev/guide/scenes/components#components-from-plugins-and-scripts", Documentation<Lantern>());

    private static string Documentation<T>() => ComponentSections.DocumentationOf(new ReflectionComponentDefinition<T>(Converters)).ToString();

    [Component]
    public struct Lantern;
}
