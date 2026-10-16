using Griff.Agent;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// What kind of field a property is, worked out once by <see cref="PropertyDescriptor.Kind"/> (from its type, its
/// attributes and its name), is the answer the Inspector and <c>component_describe</c> both give: every field of every
/// component gets the Inspector's editor of that kind and is described to agents as that kind, so a string called
/// JumpSound is a sound to both.
/// </summary>
public sealed class FieldKindTests
{
    [Fact]
    public void TheDescriptorsKind_DrivesTheInspectorAndComponentDescribeAlike() => EditorHost.Run(services =>
    {
        var registry = services.GetRequiredService<Workspace>().Components;
        var host = services.GetRequiredService<InspectorPanel>();
        var problems = new List<string>();
        var checkedFields = 0;
        foreach (var descriptor in registry.All)
        {
            var entity = new Entity("Sample");
            var component = descriptor.CreateInstance();
            if (component is Transform)
                component = entity.Transform;
            else
                entity.AddComponent(component);
            var text = ComponentDescription.Of(registry, descriptor);
            foreach (var property in descriptor.Properties)
            {
                var name = $"{descriptor.DisplayName}.{property.Name}";
                var field = new PropertyField(component, property, host);
                if (field.Kind != property.Kind)
                    problems.Add($"{name}: the Inspector shows {field.Kind}, the descriptor says {property.Kind}.");
                if ((field.Kind == FieldKind.Custom) != (field.Editor is not null))
                    problems.Add($"{name}: {field.Kind} with{(field.Editor is null ? "out" : "")} an editor of its own.");
                var line = Entry(text, property);
                if (line is null || !line.EndsWith($"(Field: {ComponentDescription.KindText(property)}.)", StringComparison.Ordinal))
                    problems.Add($"{name}: component_describe doesn't end its line with its kind, \"{ComponentDescription.KindText(property)}\": {line}");
                checkedFields++;
            }
        }

        Assert.True(checkedFields > 500, $"Only {checkedFields} fields.");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    });

    [Theory]
    [InlineData(typeof(PlatformerMovement), nameof(PlatformerMovement.JumpSound), FieldKind.Sound, AssetFileKind.Sound, "a sound", "(A sound in Assets")]
    [InlineData(typeof(BackgroundMusic), nameof(BackgroundMusic.Music), FieldKind.Sound, AssetFileKind.Music, "music", "(Music in Assets")]
    [InlineData(typeof(Sprite), nameof(Sprite.Image), FieldKind.Image, AssetFileKind.Image, "an image", "(A picture in Assets")]
    public void AStringsNameSaysWhatFileItNames_ToTheInspectorAndAgentsAlike(Type type, string property, FieldKind kind, AssetFileKind file, string word, string note) =>
        EditorHost.Run(services =>
        {
            var registry = services.GetRequiredService<Workspace>().Components;
            var descriptor = registry.Get(type);
            var field = descriptor.Properties.Single(p => p.Name == property);
            Assert.Equal(kind, field.Kind);
            Assert.Equal(file, field.File?.Kind);

            var component = descriptor.CreateInstance();
            new Entity("Sample").AddComponent(component);
            Assert.Equal(kind, new PropertyField(component, field, services.GetRequiredService<InspectorPanel>()).Kind);
            var line = Entry(ComponentDescription.Of(registry, descriptor), field);
            Assert.NotNull(line);
            Assert.Contains(note, line);
            Assert.EndsWith($"(Field: {word}.)", line);
        });

    // What component_describe says of a property: its entry under Properties, from its key to the next entry or heading
    // (its meaning can run over several lines).
    private static readonly string[] EntryEnds = ["\n- ", "\n#", "\n\n"];

    private static string? Entry(string text, PropertyDescriptor property)
    {
        text = text.Replace("\r", "", StringComparison.Ordinal);
        var start = text.IndexOf($"\n- {property.SerializedName}: ", StringComparison.Ordinal);
        if (start < 0)
            return null;
        var end = EntryEnds.Select(e => text.IndexOf(e, start + 1, StringComparison.Ordinal)).Where(e => e > 0).DefaultIfEmpty(text.Length).Min();
        return text[(start + 1)..end].TrimEnd();
    }
}
