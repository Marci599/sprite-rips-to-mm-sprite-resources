using FramesToMMSpriteResources.DataConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace FramesToMMSpriteResources;

    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        IncludeFields = true,
        WriteIndented = true
    )]
    [JsonSerializable(typeof(ProgramConfig))]
    [JsonSerializable(typeof(SubjectConfig))]
    [JsonSerializable(typeof(SubjectInterfaceConfig))]
    [JsonSerializable(typeof(SheetConfig))]
    [JsonSerializable(typeof(AssetConfig))]
    [JsonSerializable(typeof(AnimationConfig))]
    [JsonSerializable(typeof(AnimationInterfaceConfig))]
    [JsonSerializable(typeof(InterfaceConfig))]
    [JsonSerializable(typeof(ParentConfig))]
    internal partial class ConfigJsonContext : JsonSerializerContext
    {
    }

