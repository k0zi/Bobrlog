using System.Text.Json;
using System.Text.Json.Serialization;
using Bobrlog.Core.Models;
using Bobrlog.Core.Rules;

namespace Bobrlog.Core;

/// <summary>Source-generated JSON metadata (trim/AOT safe) for rules and the service protocol.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<EventRule>))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
