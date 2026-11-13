using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Ashana.Application.State;
using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.Domain.Users;

namespace Ashana.Infrastructure.Persistence;

/// <summary>The shape of the data file: plain lists, easy to read in any text editor.</summary>
internal sealed record StateFile(
    int FormatVersion,
    List<User> Users,
    List<Category> Categories,
    List<MenuItem> MenuItems,
    List<Order> Orders,
    CanteenSettings Settings);

/// <summary>
/// Converts the immutable state to JSON and back (System.Text.Json with stream-based I/O).
/// Domain records keep their setters <c>private init</c>; a contract modifier lets the serializer use them,
/// so the domain needs no JSON attributes at all.
/// </summary>
public static class StateSerializer
{
    public const int FormatVersion = 2;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { IncludePrivateState } },
    };

    public static async Task WriteAsync(CanteenState state, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var file = new StateFile(
            FormatVersion,
            [.. state.Users.Values],
            [.. state.Categories.Values],
            [.. state.MenuItems.Values],
            [.. state.Orders.Values],
            state.Settings);
        await JsonSerializer.SerializeAsync(stream, file, Options, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<CanteenState> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var file = await JsonSerializer.DeserializeAsync<StateFile>(stream, Options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The data file is empty.");

        return new CanteenState(
            file.Users.ToImmutableDictionary(u => u.Id),
            file.Categories.ToImmutableDictionary(c => c.Id),
            file.MenuItems.ToImmutableDictionary(i => i.Id),
            file.Orders.ToImmutableDictionary(o => o.Id),
            file.Settings);
    }

    public static string ToJson(CanteenState state)
    {
        using var stream = new MemoryStream();
        WriteAsync(state, stream).GetAwaiter().GetResult();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static CanteenState FromJson(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return ReadAsync(stream).GetAwaiter().GetResult();
    }

    private static void IncludePrivateState(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || info.Type.Namespace?.StartsWith("Ashana.Domain", StringComparison.Ordinal) != true)
        {
            return;
        }

        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Domain records have private constructors (they are created through validating factories).
        if (info.CreateObject is null && info.Type.GetConstructor(All, Type.EmptyTypes) is { } constructor)
        {
            info.CreateObject = () => constructor.Invoke(null);
        }

        foreach (var property in info.Properties)
        {
            if (property.Set is not null)
            {
                continue;
            }

            // AttributeProvider is the CLR property behind the JSON property (its JSON name is camelCase).
            var setter = (property.AttributeProvider as PropertyInfo)?.GetSetMethod(nonPublic: true);
            if (setter is not null)
            {
                property.Set = (target, value) => setter.Invoke(target, [value]);
            }
            else
            {
                // Derived values (Total, Code, IsOrderable...) are recomputed, so they are not stored.
                property.ShouldSerialize = static (_, _) => false;
            }
        }
    }
}
