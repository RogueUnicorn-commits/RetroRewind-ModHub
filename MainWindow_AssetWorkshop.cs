using System.Windows.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CUE4Parse.Compression;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Sound;
using CUE4Parse.UE4.Assets.Exports.Wwise;
using CUE4Parse.UE4.Wwise;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Sounds;
using LibVLCSharp.Shared;
using System.Windows.Input;

namespace RogueUnicorn.StoreTransfer;

public partial class MainWindow
{
    private sealed record AssetPakItem(string Path, string DisplayName);
    private sealed record AssetEntryItem(string Path, string AssetName, string AssetType, long? Size, long? CompressedSize, string Compression, bool Encrypted, int FileCount, string SourceClass = "")
    {
        public string DisplaySize => Size.HasValue ? FormatAssetSize(Size.Value) : "—";
        public string DisplayCompression => string.IsNullOrWhiteSpace(Compression) ? "None" : Compression;
        public string DisplayStatus => Encrypted ? "Encrypted" : "Ready";
        public string DisplayFiles => FileCount <= 1 ? "1 file" : $"{FileCount} files";
    }

    private sealed record AssetWorkshopProjectAsset(
        string AssetPath,
        string AssetName,
        string AssetType,
        string ReplacementFile);

    private sealed record AssetWorkshopProjectFile(
        string ModName,
        string AssetType,
        DateTime SavedAtUtc,
        List<AssetWorkshopProjectAsset> Assets);

    private bool _assetWorkshopSaveProjectDialogMode;
    private string? _assetWorkshopLoadedProjectName;

    private List<AssetPakItem> _assetWorkshopPaks = new();
    private List<AssetEntryItem> _assetWorkshopEntries = new();
    private string? _assetWorkshopSelectedPak;
    private bool _assetWorkshopLoading;
    private string _assetWorkshopActiveType = "";

    private Button? AssetWorkshopBuildSaveButton => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureSaveButton,
        
        _ => null
    };

    private Button? AssetWorkshopBuildLoadButton => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureLoadButton,
        
        _ => null
    };

    private Grid AssetWorkshopTextureGrid => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePageGrid,
        "Static Mesh" => AssetWorkshopStaticMeshPageGrid,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPageGrid,
        "Material" => AssetWorkshopMaterialPageGrid,
        "Animation" => AssetWorkshopAnimationPageGrid,
        "Audio" => AssetWorkshopAudioPageGrid,
        "Blueprint" => AssetWorkshopBlueprintPageGrid,
        "Niagara" => AssetWorkshopNiagaraPageGrid,
        "Particle" => AssetWorkshopParticlePageGrid,
        "Widget" => AssetWorkshopWidgetPageGrid,
        "World" => AssetWorkshopWorldPageGrid,
        "Other" => AssetWorkshopOtherPageGrid,
        _ => AssetWorkshopOtherPageGrid
    };

    private TextBlock AssetWorkshopPageTitle => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePageTitle,
        "Static Mesh" => AssetWorkshopStaticMeshPageTitle,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPageTitle,
        "Material" => AssetWorkshopMaterialPageTitle,
        "Animation" => AssetWorkshopAnimationPageTitle,
        "Audio" => AssetWorkshopAudioPageTitle,
        "Blueprint" => AssetWorkshopBlueprintPageTitle,
        "Niagara" => AssetWorkshopNiagaraPageTitle,
        "Particle" => AssetWorkshopParticlePageTitle,
        "Widget" => AssetWorkshopWidgetPageTitle,
        "World" => AssetWorkshopWorldPageTitle,
        "Other" => AssetWorkshopOtherPageTitle,
        _ => AssetWorkshopOtherPageTitle
    };

    private TextBlock AssetWorkshopPageSubtitle => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePageSubtitle,
        "Static Mesh" => AssetWorkshopStaticMeshPageSubtitle,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPageSubtitle,
        "Material" => AssetWorkshopMaterialPageSubtitle,
        "Animation" => AssetWorkshopAnimationPageSubtitle,
        "Audio" => AssetWorkshopAudioPageSubtitle,
        "Blueprint" => AssetWorkshopBlueprintPageSubtitle,
        "Niagara" => AssetWorkshopNiagaraPageSubtitle,
        "Particle" => AssetWorkshopParticlePageSubtitle,
        "Widget" => AssetWorkshopWidgetPageSubtitle,
        "World" => AssetWorkshopWorldPageSubtitle,
        "Other" => AssetWorkshopOtherPageSubtitle,
        _ => AssetWorkshopOtherPageSubtitle
    };

    private TextBox AssetWorkshopSearchBox => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureSearchBox,
        "Static Mesh" => AssetWorkshopStaticMeshSearchBox,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshSearchBox,
        "Material" => AssetWorkshopMaterialSearchBox,
        "Animation" => AssetWorkshopAnimationSearchBox,
        "Audio" => AssetWorkshopAudioSearchBox,
        "Blueprint" => AssetWorkshopBlueprintSearchBox,
        "Niagara" => AssetWorkshopNiagaraSearchBox,
        "Particle" => AssetWorkshopParticleSearchBox,
        "Widget" => AssetWorkshopWidgetSearchBox,
        "World" => AssetWorkshopWorldSearchBox,
        "Other" => AssetWorkshopOtherSearchBox,
        _ => AssetWorkshopOtherSearchBox
    };

    private TextBlock AssetWorkshopEntryCount => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureEntryCount,
        "Static Mesh" => AssetWorkshopStaticMeshEntryCount,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshEntryCount,
        "Material" => AssetWorkshopMaterialEntryCount,
        "Animation" => AssetWorkshopAnimationEntryCount,
        "Audio" => AssetWorkshopAudioEntryCount,
        "Blueprint" => AssetWorkshopBlueprintEntryCount,
        "Niagara" => AssetWorkshopNiagaraEntryCount,
        "Particle" => AssetWorkshopParticleEntryCount,
        "Widget" => AssetWorkshopWidgetEntryCount,
        "World" => AssetWorkshopWorldEntryCount,
        "Other" => AssetWorkshopOtherEntryCount,
        _ => AssetWorkshopOtherEntryCount
    };

    private ListBox AssetWorkshopEntriesList => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureEntriesList,
        "Static Mesh" => AssetWorkshopStaticMeshEntriesList,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshEntriesList,
        "Material" => AssetWorkshopMaterialEntriesList,
        "Animation" => AssetWorkshopAnimationEntriesList,
        "Audio" => AssetWorkshopAudioEntriesList,
        "Blueprint" => AssetWorkshopBlueprintEntriesList,
        "Niagara" => AssetWorkshopNiagaraEntriesList,
        "Particle" => AssetWorkshopParticleEntriesList,
        "Widget" => AssetWorkshopWidgetEntriesList,
        "World" => AssetWorkshopWorldEntriesList,
        "Other" => AssetWorkshopOtherEntriesList,
        _ => AssetWorkshopOtherEntriesList
    };

    private ToggleButton AssetWorkshopBuildModeToggle => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureBuildModeToggle,
        
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshBuildModeToggle,
        "Material" => AssetWorkshopMaterialBuildModeToggle,
        "Animation" => AssetWorkshopAnimationBuildModeToggle,
        "Audio" => AssetWorkshopAudioBuildModeToggle,
        "Blueprint" => AssetWorkshopBlueprintBuildModeToggle,
        "Niagara" => AssetWorkshopNiagaraBuildModeToggle,
        "Particle" => AssetWorkshopParticleBuildModeToggle,
        "Widget" => AssetWorkshopWidgetBuildModeToggle,
        "World" => AssetWorkshopWorldBuildModeToggle,
        "Other" => AssetWorkshopOtherBuildModeToggle,
        _ => AssetWorkshopOtherBuildModeToggle
    };

    private Button AssetWorkshopExtractButton => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureExtractButton,
        "Static Mesh" => AssetWorkshopStaticMeshExtractButton,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshExtractButton,
        "Material" => AssetWorkshopMaterialExtractButton,
        "Animation" => AssetWorkshopAnimationExtractButton,
        "Audio" => AssetWorkshopAudioExtractButton,
        "Blueprint" => AssetWorkshopBlueprintExtractButton,
        "Niagara" => AssetWorkshopNiagaraExtractButton,
        "Particle" => AssetWorkshopParticleExtractButton,
        "Widget" => AssetWorkshopWidgetExtractButton,
        "World" => AssetWorkshopWorldExtractButton,
        "Other" => AssetWorkshopOtherExtractButton,
        _ => AssetWorkshopOtherExtractButton
    };

    private Button AssetWorkshopBuildPakButton => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureBuildPakButton,
        
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshBuildPakButton,
        "Material" => AssetWorkshopMaterialBuildPakButton,
        "Animation" => AssetWorkshopAnimationBuildPakButton,
        "Audio" => AssetWorkshopAudioBuildPakButton,
        "Blueprint" => AssetWorkshopBlueprintBuildPakButton,
        "Niagara" => AssetWorkshopNiagaraBuildPakButton,
        "Particle" => AssetWorkshopParticleBuildPakButton,
        "Widget" => AssetWorkshopWidgetBuildPakButton,
        "World" => AssetWorkshopWorldBuildPakButton,
        "Other" => AssetWorkshopOtherBuildPakButton,
        _ => AssetWorkshopOtherBuildPakButton
    };

    private TextBlock AssetWorkshopStatus => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureStatus,
        "Static Mesh" => AssetWorkshopStaticMeshStatus,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshStatus,
        "Material" => AssetWorkshopMaterialStatus,
        "Animation" => AssetWorkshopAnimationStatus,
        "Audio" => AssetWorkshopAudioStatus,
        "Blueprint" => AssetWorkshopBlueprintStatus,
        "Niagara" => AssetWorkshopNiagaraStatus,
        "Particle" => AssetWorkshopParticleStatus,
        "Widget" => AssetWorkshopWidgetStatus,
        "World" => AssetWorkshopWorldStatus,
        "Other" => AssetWorkshopOtherStatus,
        _ => AssetWorkshopOtherStatus
    };

    private Border AssetWorkshopBuildPreviewPanel => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureBuildPreviewPanel,
        
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshBuildPreviewPanel,
        "Material" => AssetWorkshopMaterialBuildPreviewPanel,
        "Animation" => AssetWorkshopAnimationBuildPreviewPanel,
        "Audio" => AssetWorkshopAudioBuildPreviewPanel,
        "Blueprint" => AssetWorkshopBlueprintBuildPreviewPanel,
        "Niagara" => AssetWorkshopNiagaraBuildPreviewPanel,
        "Particle" => AssetWorkshopParticleBuildPreviewPanel,
        "Widget" => AssetWorkshopWidgetBuildPreviewPanel,
        "World" => AssetWorkshopWorldBuildPreviewPanel,
        "Other" => AssetWorkshopOtherBuildPreviewPanel,
        _ => AssetWorkshopOtherBuildPreviewPanel
    };

    private Image AssetWorkshopOriginalBuildImage => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureOriginalBuildImage,
        
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshOriginalBuildImage,
        "Material" => AssetWorkshopMaterialOriginalBuildImage,
        "Animation" => AssetWorkshopAnimationOriginalBuildImage,
        "Audio" => AssetWorkshopAudioOriginalBuildImage,
        "Blueprint" => AssetWorkshopBlueprintOriginalBuildImage,
        "Niagara" => AssetWorkshopNiagaraOriginalBuildImage,
        "Particle" => AssetWorkshopParticleOriginalBuildImage,
        "Widget" => AssetWorkshopWidgetOriginalBuildImage,
        "World" => AssetWorkshopWorldOriginalBuildImage,
        "Other" => AssetWorkshopOtherOriginalBuildImage,
        _ => AssetWorkshopOtherOriginalBuildImage
    };

    private TextBlock AssetWorkshopOriginalBuildInfo => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureOriginalBuildInfo,
        
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshOriginalBuildInfo,
        "Material" => AssetWorkshopMaterialOriginalBuildInfo,
        "Animation" => AssetWorkshopAnimationOriginalBuildInfo,
        "Audio" => AssetWorkshopAudioOriginalBuildInfo,
        "Blueprint" => AssetWorkshopBlueprintOriginalBuildInfo,
        "Niagara" => AssetWorkshopNiagaraOriginalBuildInfo,
        "Particle" => AssetWorkshopParticleOriginalBuildInfo,
        "Widget" => AssetWorkshopWidgetOriginalBuildInfo,
        "World" => AssetWorkshopWorldOriginalBuildInfo,
        "Other" => AssetWorkshopOtherOriginalBuildInfo,
        _ => AssetWorkshopOtherOriginalBuildInfo
    };

    private Image AssetWorkshopReplacementBuildImage => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureReplacementBuildImage,
        
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshReplacementBuildImage,
        "Material" => AssetWorkshopMaterialReplacementBuildImage,
        "Animation" => AssetWorkshopAnimationReplacementBuildImage,
        "Audio" => AssetWorkshopAudioReplacementBuildImage,
        "Blueprint" => AssetWorkshopBlueprintReplacementBuildImage,
        "Niagara" => AssetWorkshopNiagaraReplacementBuildImage,
        "Particle" => AssetWorkshopParticleReplacementBuildImage,
        "Widget" => AssetWorkshopWidgetReplacementBuildImage,
        "World" => AssetWorkshopWorldReplacementBuildImage,
        "Other" => AssetWorkshopOtherReplacementBuildImage,
        _ => AssetWorkshopOtherReplacementBuildImage
    };

    private TextBlock AssetWorkshopReplacementBuildInfo => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureReplacementBuildInfo,
        
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshReplacementBuildInfo,
        "Material" => AssetWorkshopMaterialReplacementBuildInfo,
        "Animation" => AssetWorkshopAnimationReplacementBuildInfo,
        "Audio" => AssetWorkshopAudioReplacementBuildInfo,
        "Blueprint" => AssetWorkshopBlueprintReplacementBuildInfo,
        "Niagara" => AssetWorkshopNiagaraReplacementBuildInfo,
        "Particle" => AssetWorkshopParticleReplacementBuildInfo,
        "Widget" => AssetWorkshopWidgetReplacementBuildInfo,
        "World" => AssetWorkshopWorldReplacementBuildInfo,
        "Other" => AssetWorkshopOtherReplacementBuildInfo,
        _ => AssetWorkshopOtherReplacementBuildInfo
    };

    private Border AssetWorkshopStandardTexturePreviewPanel => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureStandardTexturePreviewPanel,
        "Static Mesh" => AssetWorkshopStaticMeshStandardTexturePreviewPanel,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshStandardTexturePreviewPanel,
        "Material" => AssetWorkshopMaterialStandardTexturePreviewPanel,
        "Animation" => AssetWorkshopAnimationStandardTexturePreviewPanel,
        "Audio" => AssetWorkshopAudioStandardTexturePreviewPanel,
        "Blueprint" => AssetWorkshopBlueprintStandardTexturePreviewPanel,
        "Niagara" => AssetWorkshopNiagaraStandardTexturePreviewPanel,
        "Particle" => AssetWorkshopParticleStandardTexturePreviewPanel,
        "Widget" => AssetWorkshopWidgetStandardTexturePreviewPanel,
        "World" => AssetWorkshopWorldStandardTexturePreviewPanel,
        "Other" => AssetWorkshopOtherStandardTexturePreviewPanel,
        _ => AssetWorkshopOtherStandardTexturePreviewPanel
    };

    private TextBlock AssetWorkshopPreviewName => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewName,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewName,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewName,
        "Material" => AssetWorkshopMaterialPreviewName,
        "Animation" => AssetWorkshopAnimationPreviewName,
        "Audio" => AssetWorkshopAudioPreviewName,
        "Blueprint" => AssetWorkshopBlueprintPreviewName,
        "Niagara" => AssetWorkshopNiagaraPreviewName,
        "Particle" => AssetWorkshopParticlePreviewName,
        "Widget" => AssetWorkshopWidgetPreviewName,
        "World" => AssetWorkshopWorldPreviewName,
        "Other" => AssetWorkshopOtherPreviewName,
        _ => AssetWorkshopOtherPreviewName
    };

    private Image AssetWorkshopPreviewImage => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewImage,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewImage,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewImage,
        "Material" => AssetWorkshopMaterialPreviewImage,
        "Animation" => AssetWorkshopAnimationPreviewImage,
        "Audio" => AssetWorkshopAudioPreviewImage,
        "Blueprint" => AssetWorkshopBlueprintPreviewImage,
        "Niagara" => AssetWorkshopNiagaraPreviewImage,
        "Particle" => AssetWorkshopParticlePreviewImage,
        "Widget" => AssetWorkshopWidgetPreviewImage,
        "World" => AssetWorkshopWorldPreviewImage,
        "Other" => AssetWorkshopOtherPreviewImage,
        _ => AssetWorkshopOtherPreviewImage
    };

    private TextBlock AssetWorkshopPreviewStatus => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewStatus,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewStatus,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewStatus,
        "Material" => AssetWorkshopMaterialPreviewStatus,
        "Animation" => AssetWorkshopAnimationPreviewStatus,
        "Audio" => AssetWorkshopAudioPreviewStatus,
        "Blueprint" => AssetWorkshopBlueprintPreviewStatus,
        "Niagara" => AssetWorkshopNiagaraPreviewStatus,
        "Particle" => AssetWorkshopParticlePreviewStatus,
        "Widget" => AssetWorkshopWidgetPreviewStatus,
        "World" => AssetWorkshopWorldPreviewStatus,
        "Other" => AssetWorkshopOtherPreviewStatus,
        _ => AssetWorkshopOtherPreviewStatus
    };

    private StackPanel AssetWorkshopAudioControls => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureAudioControls,
        "Static Mesh" => AssetWorkshopStaticMeshAudioControls,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshAudioControls,
        "Material" => AssetWorkshopMaterialAudioControls,
        "Animation" => AssetWorkshopAnimationAudioControls,
        "Audio" => AssetWorkshopAudioAudioControls,
        "Blueprint" => AssetWorkshopBlueprintAudioControls,
        "Niagara" => AssetWorkshopNiagaraAudioControls,
        "Particle" => AssetWorkshopParticleAudioControls,
        "Widget" => AssetWorkshopWidgetAudioControls,
        "World" => AssetWorkshopWorldAudioControls,
        "Other" => AssetWorkshopOtherAudioControls,
        _ => AssetWorkshopOtherAudioControls
    };

    private Slider AssetWorkshopAudioProgress => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureAudioProgress,
        "Static Mesh" => AssetWorkshopStaticMeshAudioProgress,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshAudioProgress,
        "Material" => AssetWorkshopMaterialAudioProgress,
        "Animation" => AssetWorkshopAnimationAudioProgress,
        "Audio" => AssetWorkshopAudioAudioProgress,
        "Blueprint" => AssetWorkshopBlueprintAudioProgress,
        "Niagara" => AssetWorkshopNiagaraAudioProgress,
        "Particle" => AssetWorkshopParticleAudioProgress,
        "Widget" => AssetWorkshopWidgetAudioProgress,
        "World" => AssetWorkshopWorldAudioProgress,
        "Other" => AssetWorkshopOtherAudioProgress,
        _ => AssetWorkshopOtherAudioProgress
    };

    private TextBlock AssetWorkshopAudioTime => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureAudioTime,
        "Static Mesh" => AssetWorkshopStaticMeshAudioTime,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshAudioTime,
        "Material" => AssetWorkshopMaterialAudioTime,
        "Animation" => AssetWorkshopAnimationAudioTime,
        "Audio" => AssetWorkshopAudioAudioTime,
        "Blueprint" => AssetWorkshopBlueprintAudioTime,
        "Niagara" => AssetWorkshopNiagaraAudioTime,
        "Particle" => AssetWorkshopParticleAudioTime,
        "Widget" => AssetWorkshopWidgetAudioTime,
        "World" => AssetWorkshopWorldAudioTime,
        "Other" => AssetWorkshopOtherAudioTime,
        _ => AssetWorkshopOtherAudioTime
    };

    private Slider AssetWorkshopAudioVolume => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTextureAudioVolume,
        "Static Mesh" => AssetWorkshopStaticMeshAudioVolume,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshAudioVolume,
        "Material" => AssetWorkshopMaterialAudioVolume,
        "Animation" => AssetWorkshopAnimationAudioVolume,
        "Audio" => AssetWorkshopAudioAudioVolume,
        "Blueprint" => AssetWorkshopBlueprintAudioVolume,
        "Niagara" => AssetWorkshopNiagaraAudioVolume,
        "Particle" => AssetWorkshopParticleAudioVolume,
        "Widget" => AssetWorkshopWidgetAudioVolume,
        "World" => AssetWorkshopWorldAudioVolume,
        "Other" => AssetWorkshopOtherAudioVolume,
        _ => AssetWorkshopOtherAudioVolume
    };

    private TextBlock AssetWorkshopPreviewType => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewType,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewType,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewType,
        "Material" => AssetWorkshopMaterialPreviewType,
        "Animation" => AssetWorkshopAnimationPreviewType,
        "Audio" => AssetWorkshopAudioPreviewType,
        "Blueprint" => AssetWorkshopBlueprintPreviewType,
        "Niagara" => AssetWorkshopNiagaraPreviewType,
        "Particle" => AssetWorkshopParticlePreviewType,
        "Widget" => AssetWorkshopWidgetPreviewType,
        "World" => AssetWorkshopWorldPreviewType,
        "Other" => AssetWorkshopOtherPreviewType,
        _ => AssetWorkshopOtherPreviewType
    };

    private TextBlock AssetWorkshopPreviewSize => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewSize,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewSize,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewSize,
        "Material" => AssetWorkshopMaterialPreviewSize,
        "Animation" => AssetWorkshopAnimationPreviewSize,
        "Audio" => AssetWorkshopAudioPreviewSize,
        "Blueprint" => AssetWorkshopBlueprintPreviewSize,
        "Niagara" => AssetWorkshopNiagaraPreviewSize,
        "Particle" => AssetWorkshopParticlePreviewSize,
        "Widget" => AssetWorkshopWidgetPreviewSize,
        "World" => AssetWorkshopWorldPreviewSize,
        "Other" => AssetWorkshopOtherPreviewSize,
        _ => AssetWorkshopOtherPreviewSize
    };

    private TextBlock AssetWorkshopPreviewPath => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewPath,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewPath,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewPath,
        "Material" => AssetWorkshopMaterialPreviewPath,
        "Animation" => AssetWorkshopAnimationPreviewPath,
        "Audio" => AssetWorkshopAudioPreviewPath,
        "Blueprint" => AssetWorkshopBlueprintPreviewPath,
        "Niagara" => AssetWorkshopNiagaraPreviewPath,
        "Particle" => AssetWorkshopParticlePreviewPath,
        "Widget" => AssetWorkshopWidgetPreviewPath,
        "World" => AssetWorkshopWorldPreviewPath,
        "Other" => AssetWorkshopOtherPreviewPath,
        _ => AssetWorkshopOtherPreviewPath
    };

    private TextBlock AssetWorkshopPreviewSourceClass => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewSourceClass,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewSourceClass,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewSourceClass,
        "Material" => AssetWorkshopMaterialPreviewSourceClass,
        "Animation" => AssetWorkshopAnimationPreviewSourceClass,
        "Audio" => AssetWorkshopAudioPreviewSourceClass,
        "Blueprint" => AssetWorkshopBlueprintPreviewSourceClass,
        "Niagara" => AssetWorkshopNiagaraPreviewSourceClass,
        "Particle" => AssetWorkshopParticlePreviewSourceClass,
        "Widget" => AssetWorkshopWidgetPreviewSourceClass,
        "World" => AssetWorkshopWorldPreviewSourceClass,
        "Other" => AssetWorkshopOtherPreviewSourceClass,
        _ => AssetWorkshopOtherPreviewSourceClass
    };

    private TextBlock AssetWorkshopPreviewFiles => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewFiles,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewFiles,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewFiles,
        "Material" => AssetWorkshopMaterialPreviewFiles,
        "Animation" => AssetWorkshopAnimationPreviewFiles,
        "Audio" => AssetWorkshopAudioPreviewFiles,
        "Blueprint" => AssetWorkshopBlueprintPreviewFiles,
        "Niagara" => AssetWorkshopNiagaraPreviewFiles,
        "Particle" => AssetWorkshopParticlePreviewFiles,
        "Widget" => AssetWorkshopWidgetPreviewFiles,
        "World" => AssetWorkshopWorldPreviewFiles,
        "Other" => AssetWorkshopOtherPreviewFiles,
        _ => AssetWorkshopOtherPreviewFiles
    };

    private TextBlock AssetWorkshopPreviewMeshPreview => _assetWorkshopActiveType switch
    {
        "Texture" => AssetWorkshopTexturePreviewMeshPreview,
        "Static Mesh" => AssetWorkshopStaticMeshPreviewMeshPreview,
        "Skeletal Mesh" => AssetWorkshopSkeletalMeshPreviewMeshPreview,
        "Material" => AssetWorkshopMaterialPreviewMeshPreview,
        "Animation" => AssetWorkshopAnimationPreviewMeshPreview,
        "Audio" => AssetWorkshopAudioPreviewMeshPreview,
        "Blueprint" => AssetWorkshopBlueprintPreviewMeshPreview,
        "Niagara" => AssetWorkshopNiagaraPreviewMeshPreview,
        "Particle" => AssetWorkshopParticlePreviewMeshPreview,
        "Widget" => AssetWorkshopWidgetPreviewMeshPreview,
        "World" => AssetWorkshopWorldPreviewMeshPreview,
        "Other" => AssetWorkshopOtherPreviewMeshPreview,
        _ => AssetWorkshopOtherPreviewMeshPreview
    };

    private static readonly string[] AssetWorkshopTypes =
    {
        "Texture", "Static Mesh", "Skeletal Mesh", "Material", "Animation",
        "Audio", "Blueprint", "Niagara", "Particle", "Widget", "World", "Other"
    };

    private static bool IsAssetWorkshopCategoryMode(string mode) =>
        mode.StartsWith("asset_", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssetWorkshopFullPageType(string type) =>
        string.Equals(type, "Texture", StringComparison.OrdinalIgnoreCase);

    private static bool IsAssetWorkshopTexturePath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        return name.StartsWith("T_", StringComparison.OrdinalIgnoreCase) &&
               name.EndsWith("_bc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssetWorkshopStaticMeshPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        return name.StartsWith("LA_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("SM_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssetWorkshopPathAllowed(string path, string assetType)
    {
        if (string.Equals(assetType, "Texture", StringComparison.OrdinalIgnoreCase))
            return IsAssetWorkshopTexturePath(path);
        if (string.Equals(assetType, "Static Mesh", StringComparison.OrdinalIgnoreCase))
            return IsAssetWorkshopStaticMeshPath(path);
        return true;
    }
    private static readonly JsonSerializerOptions AssetWorkshopCacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    private LibVLC? _assetWorkshopAudioLibVlc;
    private LibVLCSharp.Shared.MediaPlayer? _assetWorkshopAudioPlayer;
    private Media? _assetWorkshopAudioMedia;
    private string? _assetWorkshopAudioFile;
    private readonly System.Windows.Threading.DispatcherTimer _assetWorkshopAudioTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

    private static string FormatAssetSize(long value)
    {
        if (value < 1024) return $"{value} B";
        if (value < 1024 * 1024) return $"{value / 1024d:0.0} KB";
        if (value < 1024L * 1024 * 1024) return $"{value / (1024d * 1024d):0.0} MB";
        return $"{value / (1024d * 1024d * 1024d):0.00} GB";
    }

    private void RefreshAssetWorkshopPage()
    {
        EnsureAssetWorkshopAudioTimer();
        if (!string.Equals(_mode, "assets", StringComparison.OrdinalIgnoreCase) &&
            !IsAssetWorkshopCategoryMode(_mode)) return;
        SetAssetWorkshopCategoryButtonState();
        _ = LoadAssetWorkshopPaksAsync();
    }

    private async Task LoadAssetWorkshopPaksAsync(bool forceRefresh = false)
    {
        if (_assetWorkshopLoading) return;
        _assetWorkshopLoading = true;
        var activeType = _assetWorkshopActiveType;
        try
        {
            AssetWorkshopStatus.Text = $"Finding the vanilla game PAK for {GetAssetWorkshopDisplayType(activeType)}…";
            AssetWorkshopEntriesList.Items.Clear();
            AssetWorkshopEntryCount.Text = "";
            AssetWorkshopExtractButton.IsEnabled = false;
            ClearAssetWorkshopPreview();

            var root = GetVerifiedGameRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                AssetWorkshopStatus.Text = "Set and verify your Retro Rewind game folder first.";
                return;
            }

            var pakPath = await Task.Run(() => FindVanillaRetroRewindPak(root));
            if (string.IsNullOrWhiteSpace(pakPath))
            {
                AssetWorkshopStatus.Text = "RetroRewind-Windows.pak was not found in RetroRewind\\Content\\Paks.";
                return;
            }

            _assetWorkshopSelectedPak = pakPath;
            AssetWorkshopStatus.Text = forceRefresh ? $"Refreshing {GetAssetWorkshopDisplayType(activeType)}…" : $"Loading {GetAssetWorkshopDisplayType(activeType)}…";

            AssetReadResult result;
            if (!forceRefresh && TryLoadAssetWorkshopCategoryCache(pakPath, activeType, out var cached))
            {
                result = cached;
                AssetWorkshopStatus.Text = $"Loaded cached {GetAssetWorkshopDisplayType(activeType)} list.";
            }
            else
            {
                result = await Task.Run(() => ReadAssetWorkshopEntries(pakPath, activeType));
                if (result.Success) TrySaveAssetWorkshopCategoryCache(pakPath, activeType, result.Entries);
            }

            if (!result.Success)
            {
                AssetWorkshopStatus.Text = result.Error;
                return;
            }

            _assetWorkshopEntries = result.Entries;
            RenderAssetWorkshopEntries();
            if (string.IsNullOrWhiteSpace(AssetWorkshopStatus.Text) ||
                AssetWorkshopStatus.Text.Contains("Loading", StringComparison.OrdinalIgnoreCase) ||
                AssetWorkshopStatus.Text.Contains("Refreshing", StringComparison.OrdinalIgnoreCase))
                AssetWorkshopStatus.Text = $"Loaded {_assetWorkshopEntries.Count:N0} {GetAssetWorkshopDisplayType(activeType)} asset(s).";
        }
        catch (Exception ex)
        {
            AssetWorkshopStatus.Text = $"Could not load {GetAssetWorkshopDisplayType(activeType)}: {ex.Message}";
        }
        finally { _assetWorkshopLoading = false; }
    }

    private static string GetAssetWorkshopDisplayType(string type) => type switch
    {
        "Texture" => "Textures",
        "Static Mesh" => "Static Meshes",
        "Skeletal Mesh" => "Skeletal Meshes",
        "Material" => "Materials",
        "Animation" => "Animations",
        "Audio" => "Audio",
        "Blueprint" => "Blueprints",
        "Niagara" => "Niagara",
        "Particle" => "Particles",
        "Widget" => "Widgets",
        "World" => "Worlds",
        "Other" => "Other",
        _ => type
    };

    private static string GetAssetWorkshopCacheDirectory()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroRewind", "AssetWorkshopCache");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetAssetWorkshopCachePath(string pakPath, string assetType)
    {
        var fullPath = Path.GetFullPath(pakPath);
        var fileInfo = new FileInfo(fullPath);
        var keyText = $"{fullPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}|{assetType}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(keyText))).ToLowerInvariant();
        return Path.Combine(GetAssetWorkshopCacheDirectory(), $"{hash}.json");
    }

    private static bool TryLoadAssetWorkshopCategoryCache(string pakPath, string assetType, out AssetReadResult result)
    {
        result = new AssetReadResult(false, new List<AssetEntryItem>(), false, "");
        try
        {
            var cachePath = GetAssetWorkshopCachePath(pakPath, assetType);
            if (!File.Exists(cachePath)) return false;
            var entries = JsonSerializer.Deserialize<List<AssetEntryItem>>(File.ReadAllText(cachePath), AssetWorkshopCacheJsonOptions);
            if (entries == null) return false;
            result = new AssetReadResult(true, entries, false, "");
            return true;
        }
        catch { return false; }
    }

    private static void TrySaveAssetWorkshopCategoryCache(string pakPath, string assetType, List<AssetEntryItem> entries)
    {
        try
        {
            var cachePath = GetAssetWorkshopCachePath(pakPath, assetType);
            var tempPath = cachePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(entries, AssetWorkshopCacheJsonOptions));
            File.Move(tempPath, cachePath, true);
        }
        catch { }
    }

    private void SetAssetWorkshopCategoryButtonState()
    {
        if (AssetWorkshopCategoryList == null) return;
        foreach (var button in AssetWorkshopCategoryList.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag?.ToString(), _assetWorkshopActiveType, StringComparison.OrdinalIgnoreCase);
            button.Background = selected ? FindResource("AccentBrush") as Brush : FindResource("ButtonBackgroundBrush") as Brush;
            button.Foreground = selected ? FindResource("AccentForegroundBrush") as Brush : FindResource("ForegroundBrush") as Brush;
            button.BorderBrush = selected ? FindResource("AccentBrush") as Brush : FindResource("BorderBrush") as Brush;
        }
    }


    private bool _assetWorkshopBuildMode;
    private readonly HashSet<string> _expandedAssetWorkshopTextureGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _assetWorkshopReplacements =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _assetWorkshopCustomImages =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _assetWorkshopSelectedTexturePath;

    private string AssetWorkshopTexturesFolder => Path.Combine(ModsRoot, "Textures");

    private sealed class AssetWorkshopTextureGroup
    {
        public string GroupName { get; init; } = "";
        public List<AssetEntryItem> Entries { get; init; } = new();
        public string RepresentativeName =>
            Entries.FirstOrDefault()?.AssetName ?? GroupName;
    }

    private string GetAssetWorkshopTextureGroupName(string assetName)
    {
        var parts = assetName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : assetName;
    }

    private IEnumerable<AssetWorkshopTextureGroup> GetAssetWorkshopTextureGroups(
        IEnumerable<AssetEntryItem> source)
    {
        return source
            .Where(e => e.AssetName.StartsWith("T_", StringComparison.OrdinalIgnoreCase) ||
                        e.AssetName.StartsWith("LA_", StringComparison.OrdinalIgnoreCase) ||
                        e.AssetName.StartsWith("SM_", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => GetAssetWorkshopTextureGroupName(e.AssetName),
                        StringComparer.OrdinalIgnoreCase)
            .Select(g => new AssetWorkshopTextureGroup
            {
                GroupName = g.Key,
                Entries = g.OrderBy(e => e.AssetName, StringComparer.OrdinalIgnoreCase).ToList()
            })
            .OrderBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<AssetWorkshopTextureGroup> GetAssetWorkshopAssetGroups(IEnumerable<AssetEntryItem> source, string assetType) =>
        GetAssetWorkshopTextureGroups(source);

    private bool IsAssetWorkshopGroupInReplacement(AssetWorkshopTextureGroup group)
    {
        return group.Entries.Any(e => _assetWorkshopReplacements.ContainsKey(e.Path));
    }

    private bool AreAllAssetWorkshopReplacementsCustomized()
    {
        if (_assetWorkshopReplacements.Count == 0)
            return false;

        return _assetWorkshopReplacements.Keys.All(path =>
            _assetWorkshopCustomImages.Contains(path) &&
            _assetWorkshopReplacements.TryGetValue(path, out var imagePath) &&
            !string.IsNullOrWhiteSpace(imagePath) &&
            File.Exists(imagePath));
    }

    private void UpdateAssetWorkshopBuildModeState()
    {
        if (!string.Equals(_assetWorkshopActiveType, "Texture", StringComparison.OrdinalIgnoreCase))
        {
            _assetWorkshopBuildMode = false;
            return;
        }

        if (AssetWorkshopBuildModeToggle == null) return;

        AssetWorkshopBuildModeToggle.IsEnabled = _assetWorkshopReplacements.Count > 0;

        AssetWorkshopExtractButton.Content =
            _assetWorkshopBuildMode ? "Select Image" : "Extract Asset";

        AssetWorkshopBuildPreviewPanel.Visibility =
            _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopStandardTexturePreviewPanel.Visibility =
            _assetWorkshopBuildMode ? Visibility.Collapsed : Visibility.Visible;
        if (AssetWorkshopBuildSaveButton != null)
            AssetWorkshopBuildSaveButton.Visibility =
                _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
        if (AssetWorkshopTextureResetButton != null)
            AssetWorkshopTextureResetButton.Visibility =
                _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
        if (AssetWorkshopBuildLoadButton != null)
        {
            // Load Project is available on the Textures/Static Meshes pages
            // regardless of Build Mode. It is only disabled when there are
            // no valid saved projects.
            AssetWorkshopBuildLoadButton.Visibility = Visibility.Visible;
            AssetWorkshopBuildLoadButton.IsEnabled = HasValidAssetWorkshopProjects();
        }

        if (AssetWorkshopBuildPakButton != null)
        {
            AssetWorkshopBuildPakButton.Visibility =
                _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
            AssetWorkshopBuildPakButton.IsEnabled =
                _assetWorkshopBuildMode && AreAllAssetWorkshopReplacementsCustomized();
        }

        if (AssetWorkshopExtractButton != null)
        {
            AssetWorkshopExtractButton.IsEnabled = _assetWorkshopBuildMode
                ? (_assetWorkshopSelectedTexturePath != null &&
                   _assetWorkshopReplacements.ContainsKey(_assetWorkshopSelectedTexturePath))
                : !string.IsNullOrWhiteSpace(_assetWorkshopSelectedTexturePath) &&
                  _assetWorkshopEntries.Any(e =>
                      string.Equals(
                          e.Path,
                          _assetWorkshopSelectedTexturePath,
                          StringComparison.OrdinalIgnoreCase));
        }
    }

    private void AssetWorkshopBuildModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        _assetWorkshopBuildMode = AssetWorkshopBuildModeToggle.IsChecked == true;

        AssetWorkshopExtractButton.Content =
            _assetWorkshopBuildMode ? "Select Image" : "Extract Asset";

        AssetWorkshopBuildPreviewPanel.Visibility =
            _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
        AssetWorkshopStandardTexturePreviewPanel.Visibility =
            _assetWorkshopBuildMode ? Visibility.Collapsed : Visibility.Visible;

        if (_assetWorkshopBuildMode)
        {
            AssetWorkshopReplacementBuildImage.Source = null;
            AssetWorkshopReplacementBuildInfo.Text = "No Replacement Selected";
        }

        RenderAssetWorkshopEntries();
        UpdateAssetWorkshopBuildModeState();
    }

    private Button CreateAssetWorkshopImageButton(string imagePath, string tooltip, RoutedEventHandler click)
    {
        var button = new Button
        {
            ToolTip = tooltip,
            Style = FindResource("AssetWorkshopActionButtonStyle") as Style,
            Width = 34,
            Height = 34,
            Content = new Image
            {
                Source = new BitmapImage(new Uri(imagePath, UriKind.RelativeOrAbsolute)),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform
            }
        };
        button.Click += click;
        return button;
    }

    private Grid CreateAssetWorkshopTextureGroupRow(AssetWorkshopTextureGroup group)
    {
        var outer = new Grid
        {
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0
        };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var row = new Grid { Background = Brushes.Transparent, Tag = group };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90), MinWidth = 70 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), MinWidth = 40 });

        var groupKey = group.GroupName;
        var open = _expandedAssetWorkshopTextureGroups.Contains(groupKey);

        var namePanel = new Grid { MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        namePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        namePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var chevron = new TextBlock
        {
            Text = open ? "⌄" : "›",
            FontSize = 22,
            Width = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("SecondaryBrush")
        };
        namePanel.Children.Add(chevron);

        var title = new TextBlock
        {
            Text = group.GroupName,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(title, 1);
        namePanel.Children.Add(title);

        var expand = new Button
        {
            Content = namePanel,
            MinWidth = 0,
            Height = 36,
            Style = (Style)FindResource("BrowseButtonStyle"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = group
        };
        Grid.SetColumn(expand, 0);
        row.Children.Add(expand);

        var count = new TextBlock
        {
            Text = $"{group.Entries.Count:N0} texture(s)",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 4, 0),
            Foreground = (Brush)FindResource("SecondaryBrush")
        };
        Grid.SetColumn(count, 1);
        row.Children.Add(count);

        // One action slot: Add when none of the group is selected for replacement,
        // Remove when the group is already in the replacement list.
        var groupInReplacement = IsAssetWorkshopGroupInReplacement(group);
        var action = CreateAssetWorkshopImageButton(
            groupInReplacement
                ? "pack://application:,,,/RetroRewindModhub;component/Assets/Remove.png"
                : "pack://application:,,,/RetroRewindModhub;component/Assets/Add.png",
            groupInReplacement ? $"Remove {group.GroupName}" : $"Add {group.GroupName}",
            async (_, _) =>
            {
                if (IsAssetWorkshopGroupInReplacement(group))
                    AssetWorkshopRemoveGroup(group);
                else
                    await AssetWorkshopAddGroupAsync(group);
            });
        action.Width = 34;
        action.Height = 34;
        Grid.SetColumn(action, 2);
        row.Children.Add(action);

        var children = new StackPanel
        {
            Visibility = open ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(18, 2, 0, 0)
        };
        foreach (var entry in group.Entries)
            children.Children.Add(CreateAssetWorkshopTextureChildRow(entry));

        Grid.SetRow(children, 1);
        outer.Children.Add(row);
        outer.Children.Add(children);

        expand.Click += (_, _) =>
        {
            var expanded = children.Visibility != Visibility.Visible;
            children.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            chevron.Text = expanded ? "⌄" : "›";
            if (expanded)
                _expandedAssetWorkshopTextureGroups.Add(groupKey);
            else
                _expandedAssetWorkshopTextureGroups.Remove(groupKey);
        };

        return outer;
    }

    private Grid CreateAssetWorkshopTextureChildRow(AssetEntryItem entry)
    {
        var row = new Grid
        {
            Margin = new Thickness(18, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
            Background = Brushes.Transparent,
            Tag = entry
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40), MinWidth = 40 });

        var button = new Button
        {
            Content = entry.AssetName,
            Style = (Style)FindResource("BrowseButtonStyle"),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Tag = entry,
            ToolTip = entry.Path
        };
        button.Click += async (_, _) =>
        {
            _assetWorkshopSelectedTexturePath = entry.Path;
            AssetWorkshopEntriesList.Focus();
            UpdateAssetWorkshopTextureEntryHighlights();

            if (_assetWorkshopBuildMode)
            {
                var selectedGroup = GetAssetWorkshopTextureGroups(new[] { entry }).FirstOrDefault();
                if (selectedGroup != null)
                    await LoadAssetWorkshopBuildPreviewAsync(selectedGroup);
            }
            else
            {
                await LoadAssetWorkshopTexturePreviewForEntry(entry);
            }

            UpdateAssetWorkshopBuildModeState();
        };

        if (string.Equals(_assetWorkshopSelectedTexturePath, entry.Path, StringComparison.OrdinalIgnoreCase))
        {
            button.Background = FindResource("AccentBrush") as Brush;
            button.Foreground = FindResource("AccentForegroundBrush") as Brush;
            button.BorderBrush = FindResource("AccentBrush") as Brush;
        }
        Grid.SetColumn(button, 0);
        row.Children.Add(button);

        var inReplacement = _assetWorkshopReplacements.ContainsKey(entry.Path);
        var action = CreateAssetWorkshopImageButton(
            inReplacement
                ? "pack://application:,,,/RetroRewindModhub;component/Assets/Remove.png"
                : "pack://application:,,,/RetroRewindModhub;component/Assets/Add.png",
            inReplacement ? $"Remove {entry.AssetName}" : $"Add {entry.AssetName}",
            async (_, _) =>
            {
                if (_assetWorkshopReplacements.ContainsKey(entry.Path))
                    AssetWorkshopRemoveSingle(entry);
                else
                    await AssetWorkshopAddSingleAsync(entry);
            });
        action.Width = 34;
        action.Height = 34;
        Grid.SetColumn(action, 1);
        row.Children.Add(action);

        return row;
    }


    private Grid CreateAssetWorkshopSimpleAssetRow(AssetEntryItem entry)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Background = Brushes.Transparent, Tag = entry };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        var button = new Button { Content = entry.AssetName, Style = (Style)FindResource("BrowseButtonStyle"), HorizontalContentAlignment = HorizontalAlignment.Left, Tag = entry, ToolTip = entry.Path, Height = 36 };
        button.Click += async (_, _) => await SelectAssetWorkshopEntryAsync(entry);
        Grid.SetColumn(button, 0); row.Children.Add(button);
        var size = new TextBlock { Text = entry.DisplaySize, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("SecondaryBrush"), Margin = new Thickness(8, 0, 4, 0) };
        Grid.SetColumn(size, 1); row.Children.Add(size);
        return row;
    }

    private async Task SelectAssetWorkshopEntryAsync(AssetEntryItem entry)
    {
        _assetWorkshopSelectedTexturePath = entry.Path;
        UpdateAssetWorkshopTextureEntryHighlights();
        if (string.Equals(_assetWorkshopActiveType, "Texture", StringComparison.OrdinalIgnoreCase))
        {
            await LoadAssetWorkshopTexturePreviewForEntry(entry);
            return;
        }
        StopAssetWorkshopAudio();
        HideAssetWorkshopAudioControls();
        AssetWorkshopPreviewName.Text = entry.AssetName;
        AssetWorkshopPreviewType.Text = entry.AssetType;
        AssetWorkshopPreviewSize.Text = entry.DisplaySize;
        AssetWorkshopPreviewPath.Text = entry.Path;
        AssetWorkshopPreviewSourceClass.Text = string.IsNullOrWhiteSpace(entry.SourceClass) ? entry.AssetType : entry.SourceClass;
        AssetWorkshopPreviewFiles.Text = entry.DisplayFiles;
        AssetWorkshopPreviewImage.Source = null;
        AssetWorkshopPreviewImage.Visibility = Visibility.Collapsed;
        AssetWorkshopPreviewStatus.Visibility = Visibility.Visible;
        AssetWorkshopPreviewStatus.Text = "Select Export .uasset/.uexp to extract this asset package.";
        UpdateAssetWorkshopBuildModeState();
    }

    private void UpdateAssetWorkshopTextureEntryHighlights()
    {
        if (AssetWorkshopEntriesList == null) return;

        foreach (var groupElement in AssetWorkshopEntriesList.Items.OfType<FrameworkElement>())
        {
            foreach (var button in FindVisualChildren<Button>(groupElement))
            {
                if (button.Tag is not AssetEntryItem entry) continue;

                var selected = string.Equals(
                    _assetWorkshopSelectedTexturePath,
                    entry.Path,
                    StringComparison.OrdinalIgnoreCase);

                button.Background = selected
                    ? FindResource("AccentBrush") as Brush
                    : FindResource("ButtonBackgroundBrush") as Brush;
                button.Foreground = selected
                    ? FindResource("AccentForegroundBrush") as Brush
                    : FindResource("ForegroundBrush") as Brush;
                button.BorderBrush = selected
                    ? FindResource("AccentBrush") as Brush
                    : FindResource("BorderBrush") as Brush;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T typed) return typed;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async Task AssetWorkshopAddSingleAsync(AssetEntryItem entry)
    {
        if (string.IsNullOrWhiteSpace(_assetWorkshopSelectedPak)) return;
        try
        {
            Directory.CreateDirectory(AssetWorkshopTexturesFolder);
            var outputPath = Path.Combine(AssetWorkshopTexturesFolder, entry.AssetName + ".png");
            AssetWorkshopStatus.Text = $"Extracting {entry.AssetName}…";
            await Task.Run(() => ExtractTextureAsset(_assetWorkshopSelectedPak!, entry.Path, outputPath));
            _assetWorkshopReplacements[entry.Path] = outputPath;
            _assetWorkshopCustomImages.Remove(entry.Path);
            RenderAssetWorkshopEntries();
            UpdateAssetWorkshopBuildModeState();
        }
        catch (Exception ex)
        {
            AssetWorkshopStatus.Text = $"Texture extraction failed: {ex.Message}";
        }
    }

    private void AssetWorkshopRemoveSingle(AssetEntryItem entry)
    {
        _assetWorkshopReplacements.Remove(entry.Path);
        _assetWorkshopCustomImages.Remove(entry.Path);
        if (string.Equals(_assetWorkshopSelectedTexturePath, entry.Path, StringComparison.OrdinalIgnoreCase))
            _assetWorkshopSelectedTexturePath = null;
        RenderAssetWorkshopEntries();
        if (_assetWorkshopReplacements.Count == 0)
            AssetWorkshopBuildModeToggle.IsChecked = false;
        else
            UpdateAssetWorkshopBuildModeState();
    }

    private async Task AssetWorkshopAddGroupAsync(AssetWorkshopTextureGroup group)
    {
        if (string.IsNullOrWhiteSpace(_assetWorkshopSelectedPak))
            return;

        try
        {
            Directory.CreateDirectory(AssetWorkshopTexturesFolder);
            AssetWorkshopStatus.Text = $"Extracting {group.GroupName} texture group…";
            AssetWorkshopExtractButton.IsEnabled = false;

            foreach (var entry in group.Entries)
            {
                var outputPath = Path.Combine(AssetWorkshopTexturesFolder, entry.AssetName + ".png");
                await Task.Run(() =>
                    ExtractTextureAsset(_assetWorkshopSelectedPak!, entry.Path, outputPath));
                _assetWorkshopReplacements[entry.Path] = outputPath;
                _assetWorkshopCustomImages.Remove(entry.Path);
            }

            AssetWorkshopStatus.Text =
                $"Added {group.Entries.Count} texture(s) from {group.GroupName} to replacement.";
            RenderAssetWorkshopEntries();
            UpdateAssetWorkshopBuildModeState();
        }
        catch (Exception ex)
        {
            AssetWorkshopStatus.Text =
                $"Texture group extraction failed: {ex.Message}";
        }
        finally
        {
            AssetWorkshopExtractButton.IsEnabled = true;
        }
    }

    private void AssetWorkshopRemoveGroup(AssetWorkshopTextureGroup group)
    {
        foreach (var entry in group.Entries)
        {
            _assetWorkshopReplacements.Remove(entry.Path);
            _assetWorkshopCustomImages.Remove(entry.Path);
        }

        AssetWorkshopOriginalBuildImage.Source = null;
        AssetWorkshopReplacementBuildImage.Source = null;
        AssetWorkshopOriginalBuildInfo.Text = "";
        AssetWorkshopReplacementBuildInfo.Text = "";

        RenderAssetWorkshopEntries();

        if (_assetWorkshopReplacements.Count == 0)
            AssetWorkshopBuildModeToggle.IsChecked = false;
        else
            UpdateAssetWorkshopBuildModeState();
    }

    private void AssetWorkshopResetBuildButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_assetWorkshopActiveType, "Texture", StringComparison.OrdinalIgnoreCase))
            return;

        _assetWorkshopReplacements.Clear();
        _assetWorkshopCustomImages.Clear();
        _assetWorkshopSelectedTexturePath = null;
        _assetWorkshopLoadedProjectName = null;

        AssetWorkshopOriginalBuildImage.Source = null;
        AssetWorkshopReplacementBuildImage.Source = null;
        AssetWorkshopOriginalBuildInfo.Text = "";
        AssetWorkshopReplacementBuildInfo.Text = "No Replacement Selected";

        AssetWorkshopBuildModeToggle.IsChecked = false;
        AssetWorkshopStatus.Text = "Build list cleared.";
    }

    private void AssetWorkshopBuildSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_assetWorkshopActiveType != "Texture" &&
            _assetWorkshopActiveType != "Static Mesh")
            return;

        if (!_assetWorkshopBuildMode)
            return;

        if (!_assetWorkshopReplacements.Any(kvp =>
                _assetWorkshopCustomImages.Contains(kvp.Key) &&
                !string.IsNullOrWhiteSpace(kvp.Value) &&
                File.Exists(kvp.Value)))
        {
            AssetWorkshopStatus.Text = "Add at least one replacement before saving a project.";
            return;
        }

        _assetWorkshopSaveProjectDialogMode = true;
        AssetWorkshopBuildPakModNameTextBox.Text = "";
        AssetWorkshopPackForNexusToggle.IsChecked = false;
        AssetWorkshopPackForNexusToggle.Visibility = Visibility.Collapsed;
        AssetWorkshopBuildPakConfirmButton.Content = "Save Project";
        AssetWorkshopBuildPakDialogOverlay.Visibility = Visibility.Visible;
        AssetWorkshopBuildPakModNameTextBox.Focus();
        UpdateAssetWorkshopBuildPakDialogState();
    }

    private async void AssetWorkshopBuildLoadButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowAssetWorkshopProjectsPanelAsync();
    }

    private string GetAssetWorkshopProjectsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Retro Rewind Modhub",
            "Projects");
    }

    private bool HasValidAssetWorkshopProjects()
    {
        try
        {
            var directory = GetAssetWorkshopProjectsDirectory();
            if (!Directory.Exists(directory))
                return false;

            foreach (var projectDirectory in Directory.EnumerateDirectories(
                         directory, "*", SearchOption.TopDirectoryOnly))
            {
                var jsonPath = Path.Combine(projectDirectory, "project.json");
                if (!File.Exists(jsonPath))
                    continue;

                try
                {
                    var json = File.ReadAllText(jsonPath);
                    var project = JsonSerializer.Deserialize<AssetWorkshopProjectFile>(json);
                    if (project == null ||
                        string.IsNullOrWhiteSpace(project.ModName) ||
                        (project.AssetType != "Texture" && project.AssetType != "Static Mesh") ||
                        project.Assets == null)
                        continue;

                    // A project is considered valid only when its replacement
                    // files are actually present beside project.json.
                    if (project.Assets.Any(asset =>
                            !string.IsNullOrWhiteSpace(asset.AssetPath) &&
                            !string.IsNullOrWhiteSpace(asset.ReplacementFile) &&
                            File.Exists(Path.Combine(projectDirectory, asset.ReplacementFile))))
                        return true;
                }
                catch
                {
                    // Ignore malformed project.json files and continue looking.
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private async Task SaveAssetWorkshopProjectAsync(string modName)
    {
        var projectsRoot = GetAssetWorkshopProjectsDirectory();
        var projectDirectory = Path.Combine(projectsRoot, modName);
        Directory.CreateDirectory(projectDirectory);

        var assets = new List<AssetWorkshopProjectAsset>();
        foreach (var kvp in _assetWorkshopReplacements
                     .Where(kvp =>
                         _assetWorkshopCustomImages.Contains(kvp.Key) &&
                         !string.IsNullOrWhiteSpace(kvp.Value) &&
                         File.Exists(kvp.Value)))
        {
            var entry = _assetWorkshopEntries.FirstOrDefault(e =>
                string.Equals(e.Path, kvp.Key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                continue;

            var extension = Path.GetExtension(kvp.Value);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";

            var replacementName = SanitizeAssetWorkshopModName(entry.AssetName);
            if (string.IsNullOrWhiteSpace(replacementName))
                replacementName = "Replacement";

            var replacementFile = replacementName + extension.ToLowerInvariant();
            var destination = Path.Combine(projectDirectory, replacementFile);

            // Asset names are normally unique. If two entries share a basename,
            // keep both files without silently overwriting the first one.
            if (assets.Any(a => string.Equals(a.ReplacementFile, replacementFile, StringComparison.OrdinalIgnoreCase)))
            {
                var stem = Path.GetFileNameWithoutExtension(replacementFile);
                var n = 2;
                do
                {
                    replacementFile = $"{stem}_{n++}{extension.ToLowerInvariant()}";
                    destination = Path.Combine(projectDirectory, replacementFile);
                }
                while (File.Exists(destination) ||
                       assets.Any(a => string.Equals(a.ReplacementFile, replacementFile, StringComparison.OrdinalIgnoreCase)));
            }

            File.Copy(kvp.Value, destination, true);
            assets.Add(new AssetWorkshopProjectAsset(
                entry.Path,
                entry.AssetName,
                entry.AssetType,
                replacementFile));
        }

        var project = new AssetWorkshopProjectFile(
            modName,
            _assetWorkshopActiveType,
            DateTime.UtcNow,
            assets);

        var jsonPath = Path.Combine(projectDirectory, "project.json");
        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));

        _assetWorkshopLoadedProjectName = modName;
        AssetWorkshopStatus.Text =
            $"Project saved: {Path.Combine("Documents", "Retro Rewind Modhub", "Projects", modName)}";
        UpdateAssetWorkshopBuildModeState();
    }

    private async Task ShowAssetWorkshopProjectsPanelAsync()
    {
        var directory = GetAssetWorkshopProjectsDirectory();
        Directory.CreateDirectory(directory);

        var dialog = new OverlayDialogHost(this, SlidePanelMode.Right)
        {
            Background = (Brush)Resources["CardBrush"],
            Foreground = (Brush)Resources["ForegroundBrush"]
        };

        var outer = new Grid { Margin = new Thickness(14, 18, 14, 18) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel();
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Projects",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Resources["ForegroundBrush"]
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = directory,
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11,
            Foreground = (Brush)Resources["SecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var closeButton = new Button
        {
            Content = "×",
            Width = 34,
            Height = 34,
            Style = (Style)Resources["BrowseButtonStyle"],
            ToolTip = "Close"
        };
        closeButton.Click += (_, _) => dialog.DialogResult = false;
        Grid.SetColumn(closeButton, 1);
        header.Children.Add(closeButton);
        Grid.SetRow(header, 0);
        outer.Children.Add(header);

        var refreshButton = new Button
        {
            Content = "Refresh",
            Width = 90,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            Style = (Style)Resources["BrowseButtonStyle"]
        };
        Grid.SetRow(refreshButton, 1);
        outer.Children.Add(refreshButton);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true
        };
        var list = new StackPanel();
        scroll.Content = list;
        Grid.SetRow(scroll, 2);
        outer.Children.Add(scroll);

        async Task PopulateAsync()
        {
            list.Children.Clear();

            var projects = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new DirectoryInfo(path))
                .Where(info => File.Exists(Path.Combine(info.FullName, "project.json")))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            if (projects.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "No saved projects found.",
                    Foreground = (Brush)Resources["SecondaryBrush"],
                    Margin = new Thickness(8, 18, 8, 18),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var projectDirectory in projects)
            {
                var row = new Border
                {
                    Background = (Brush)Resources["CardBrush"],
                    BorderBrush = (Brush)Resources["BorderBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel();
                info.Children.Add(new TextBlock
                {
                    Text = projectDirectory.Name,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)Resources["ForegroundBrush"]
                });

                try
                {
                    var json = await File.ReadAllTextAsync(Path.Combine(projectDirectory.FullName, "project.json"));
                    var project = JsonSerializer.Deserialize<AssetWorkshopProjectFile>(json);
                    info.Children.Add(new TextBlock
                    {
                        Text = $"{project?.AssetType ?? "Asset"} • {project?.Assets?.Count ?? 0} replacement(s)",
                        Margin = new Thickness(0, 3, 0, 0),
                        FontSize = 11,
                        Foreground = (Brush)Resources["SecondaryBrush"]
                    });
                }
                catch
                {
                    info.Children.Add(new TextBlock
                    {
                        Text = "Project file could not be read.",
                        Margin = new Thickness(0, 3, 0, 0),
                        FontSize = 11,
                        Foreground = (Brush)Resources["SecondaryBrush"]
                    });
                }

                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var loadButton = new Button
                {
                    Content = "Load",
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(8, 0, 6, 0),
                    Style = (Style)Resources["AccentButtonStyle"]
                };
                loadButton.Click += async (_, _) =>
                {
                    if (await LoadAssetWorkshopProjectAsync(projectDirectory.FullName))
                        dialog.DialogResult = true;
                };

                var deleteButton = new Button
                {
                    Content = "Delete",
                    Padding = new Thickness(12, 6, 12, 6),
                    Style = (Style)Resources["BrowseButtonStyle"]
                };
                deleteButton.Click += async (_, _) =>
                {
                    var result = MessageBox.Show(
                        this,
                        $"Delete project '{projectDirectory.Name}'?",
                        "Delete Project",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (result != MessageBoxResult.Yes)
                        return;

                    try
                    {
                        Directory.Delete(projectDirectory.FullName, true);
                        await PopulateAsync();
                        UpdateAssetWorkshopBuildModeState();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this,
                            $"The project could not be deleted.\n\n{ex.Message}",
                            "Delete Project",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };

                actions.Children.Add(loadButton);
                actions.Children.Add(deleteButton);
                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);

                row.Child = grid;
                list.Children.Add(row);
            }
        }

        refreshButton.Click += async (_, _) => await PopulateAsync();
        await PopulateAsync();

        dialog.Content = outer;
        dialog.ShowDialog();
    }

    private async Task<bool> LoadAssetWorkshopProjectAsync(string projectDirectory)
    {
        try
        {
            var jsonPath = Path.Combine(projectDirectory, "project.json");
            if (!File.Exists(jsonPath))
                throw new InvalidDataException("project.json is missing.");

            var json = await File.ReadAllTextAsync(jsonPath);
            var project = JsonSerializer.Deserialize<AssetWorkshopProjectFile>(json)
                          ?? throw new InvalidDataException("project.json is invalid.");

            if (project.AssetType != "Texture" && project.AssetType != "Static Mesh")
                throw new InvalidDataException("This project uses an unsupported Asset Workshop page.");

            _assetWorkshopLoadedProjectName = project.ModName;
            var validAssets = new List<AssetWorkshopProjectAsset>();
            foreach (var asset in project.Assets ?? new List<AssetWorkshopProjectAsset>())
            {
                if (string.IsNullOrWhiteSpace(asset.AssetPath) ||
                    string.IsNullOrWhiteSpace(asset.ReplacementFile))
                    continue;

                var replacementPath = Path.Combine(projectDirectory, asset.ReplacementFile);
                if (!File.Exists(replacementPath))
                    continue;

                validAssets.Add(asset);
            }

            if (validAssets.Count == 0)
                throw new InvalidDataException("The project contains no available replacement files.");

            _assetWorkshopBuildMode = false;
            _assetWorkshopSelectedTexturePath = null;
            _assetWorkshopActiveType = project.AssetType;
            SetAssetWorkshopCategoryButtonState();

            _assetWorkshopReplacements.Clear();
            _assetWorkshopCustomImages.Clear();

            foreach (var asset in validAssets)
            {
                _assetWorkshopReplacements[asset.AssetPath] =
                    Path.Combine(projectDirectory, asset.ReplacementFile);
                _assetWorkshopCustomImages.Add(asset.AssetPath);
            }

            _assetWorkshopBuildMode = true;
            AssetWorkshopBuildModeToggle.IsChecked = true;

            RenderAssetWorkshopEntries();
            UpdateAssetWorkshopBuildModeState();

            var first = validAssets[0];
            var firstEntry = _assetWorkshopEntries.FirstOrDefault(e =>
                string.Equals(e.Path, first.AssetPath, StringComparison.OrdinalIgnoreCase));

            if (firstEntry == null)
            {
                static string NormalizeProjectAssetPath(string value) =>
                    value.Replace('\\', '/').TrimStart('/');

                firstEntry = _assetWorkshopEntries.FirstOrDefault(e =>
                    string.Equals(
                        NormalizeProjectAssetPath(e.Path),
                        NormalizeProjectAssetPath(first.AssetPath),
                        StringComparison.OrdinalIgnoreCase));
            }

            _assetWorkshopSelectedTexturePath = firstEntry?.Path ?? first.AssetPath;
            UpdateAssetWorkshopTextureEntryHighlights();

            var group = GetAssetWorkshopTextureGroups(
                new[] { new AssetEntryItem(first.AssetPath, first.AssetName, first.AssetType, null, null, "", false, 1) })
                .FirstOrDefault();

            if (group != null)
                await LoadAssetWorkshopBuildPreviewAsync(group);

            AssetWorkshopStatus.Text =
                $"Loaded project: {project.ModName}";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"The project could not be loaded.\n\n{ex.Message}",
                "Load Project",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }


    private async void AssetWorkshopSelectImage()
    {
        if (!_assetWorkshopBuildMode ||
            string.IsNullOrWhiteSpace(_assetWorkshopSelectedTexturePath) ||
            !_assetWorkshopReplacements.ContainsKey(_assetWorkshopSelectedTexturePath))
        {
            AssetWorkshopStatus.Text = "Select a texture from the replacement list first.";
            return;
        }

        var target = _assetWorkshopEntries.FirstOrDefault(e =>
            string.Equals(e.Path, _assetWorkshopSelectedTexturePath, StringComparison.OrdinalIgnoreCase));

        if (target == null)
        {
            AssetWorkshopStatus.Text = "The selected replacement texture could not be found.";
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tga|All Files|*.*",
            Title = $"Select Replacement Image for {target.AssetName}"
        };

        if (dialog.ShowDialog() != true)
            return;

        _assetWorkshopReplacements[target.Path] = dialog.FileName;
        _assetWorkshopCustomImages.Add(target.Path);

        RenderAssetWorkshopEntries();
        UpdateAssetWorkshopTextureEntryHighlights();

        var group = GetAssetWorkshopTextureGroups(new[] { target }).FirstOrDefault();
        if (group != null)
            await LoadAssetWorkshopBuildPreviewAsync(group);

        UpdateAssetWorkshopBuildModeState();
    }

    private async Task LoadAssetWorkshopBuildPreviewAsync(AssetWorkshopTextureGroup group)
    {
        if (!_assetWorkshopBuildMode)
            return;

        try
        {
            var target = group.Entries.FirstOrDefault(e =>
                string.Equals(e.Path, _assetWorkshopSelectedTexturePath, StringComparison.OrdinalIgnoreCase))
                ?? group.Entries.FirstOrDefault();

            if (target == null)
                return;

            _assetWorkshopSelectedTexturePath = target.Path;

            // Original panel: always show the selected PAK texture.
            AssetWorkshopOriginalBuildImage.Source = null;
            AssetWorkshopOriginalBuildInfo.Text = target.AssetName;

            if (!string.IsNullOrWhiteSpace(_assetWorkshopSelectedPak))
            {
                var originalPreviewPath = await Task.Run(() =>
                    DecodeTextureAssetPreview(_assetWorkshopSelectedPak!, target.Path));

                if (!string.IsNullOrWhiteSpace(originalPreviewPath) &&
                    File.Exists(originalPreviewPath))
                {
                    AssetWorkshopOriginalBuildImage.Source =
                        await LoadAssetWorkshopBitmapAsync(originalPreviewPath);
                    AssetWorkshopOriginalBuildInfo.Text =
                        $"{target.AssetName}\n{target.DisplaySize}";
                }
                else
                {
                    AssetWorkshopOriginalBuildInfo.Text =
                        $"{target.AssetName}\nOriginal preview unavailable";
                }
            }

            // Replacement panel: this is the ONLY panel that says
            // "No Replacement Selected".
            AssetWorkshopReplacementBuildImage.Source = null;
            AssetWorkshopReplacementBuildInfo.Text = "No Replacement Selected";

            if (_assetWorkshopReplacements.TryGetValue(target.Path, out var replacementPath) &&
                _assetWorkshopCustomImages.Contains(target.Path) &&
                !string.IsNullOrWhiteSpace(replacementPath) &&
                File.Exists(replacementPath))
            {
                AssetWorkshopReplacementBuildImage.Source =
                    await LoadAssetWorkshopBitmapAsync(replacementPath);
                AssetWorkshopReplacementBuildInfo.Text =
                    $"{Path.GetFileName(replacementPath)}\nReplacement for {target.AssetName}";
            }
        }
        catch (Exception ex)
        {
            AssetWorkshopReplacementBuildImage.Source = null;
            AssetWorkshopReplacementBuildInfo.Text = "No Replacement Selected";
            AssetWorkshopStatus.Text = $"Build preview failed: {ex.Message}";
        }
    }

    private async void AssetWorkshopBuildPakButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AreAllAssetWorkshopReplacementsCustomized())
        {
            UpdateAssetWorkshopBuildModeState();
            return;
        }

        if (string.IsNullOrWhiteSpace(_assetWorkshopSelectedPak) ||
            !File.Exists(_assetWorkshopSelectedPak))
        {
            AssetWorkshopStatus.Text = "The vanilla RetroRewind-Windows.pak could not be found.";
            return;
        }

        if (string.IsNullOrWhiteSpace(FindRepakExecutable()))
        {
            AssetWorkshopStatus.Text =
                "repak.exe was not found. Place it in Documents\\Retro Rewind Modhub\\Tools\\repak.exe.";
            return;
        }


        _assetWorkshopSaveProjectDialogMode = false;
        AssetWorkshopBuildPakModNameTextBox.Text =
            !string.IsNullOrWhiteSpace(_assetWorkshopLoadedProjectName)
                ? _assetWorkshopLoadedProjectName
                : "";
        AssetWorkshopPackForNexusToggle.IsChecked = false;
        AssetWorkshopPackForNexusToggle.Visibility = Visibility.Visible;
        AssetWorkshopBuildPakConfirmButton.Content = "Build PAK";
        AssetWorkshopBuildPakDialogOverlay.Visibility = Visibility.Visible;
        AssetWorkshopBuildPakModNameTextBox.Focus();
        UpdateAssetWorkshopBuildPakDialogState();
    }

    private void AssetWorkshopBuildPakModNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateAssetWorkshopBuildPakDialogState();
    }

    private void UpdateAssetWorkshopBuildPakDialogState()
    {
        if (AssetWorkshopBuildPakConfirmButton == null ||
            AssetWorkshopBuildPakModNameTextBox == null)
            return;

        AssetWorkshopBuildPakConfirmButton.IsEnabled =
            !string.IsNullOrWhiteSpace(
                SanitizeAssetWorkshopModName(AssetWorkshopBuildPakModNameTextBox.Text));
    }

    private void AssetWorkshopBuildPakCancel_Click(object sender, RoutedEventArgs e)
    {
        _assetWorkshopSaveProjectDialogMode = false;
        AssetWorkshopPackForNexusToggle.Visibility = Visibility.Visible;
        AssetWorkshopBuildPakConfirmButton.Content = "Build PAK";
        AssetWorkshopBuildPakDialogOverlay.Visibility = Visibility.Collapsed;
    }

    private async void AssetWorkshopBuildPakConfirm_Click(object sender, RoutedEventArgs e)
    {
        var modName = SanitizeAssetWorkshopModName(
            AssetWorkshopBuildPakModNameTextBox.Text);

        if (string.IsNullOrWhiteSpace(modName))
        {
            UpdateAssetWorkshopBuildPakDialogState();
            return;
        }

        if (_assetWorkshopSaveProjectDialogMode)
        {
            try
            {
                AssetWorkshopBuildPakDialogOverlay.Visibility = Visibility.Collapsed;
                _assetWorkshopSaveProjectDialogMode = false;
                AssetWorkshopPackForNexusToggle.Visibility = Visibility.Visible;
                AssetWorkshopBuildPakConfirmButton.Content = "Build PAK";

                await SaveAssetWorkshopProjectAsync(modName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"The project could not be saved.\n\n{ex.Message}",
                    "Save Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            return;
        }

        var packForNexus = AssetWorkshopPackForNexusToggle.IsChecked == true;

        AssetWorkshopBuildPakDialogOverlay.Visibility = Visibility.Collapsed;
        AssetWorkshopBuildPakButton.IsEnabled = false;

        SetOperationBusy(true, "Building Asset Workshop PAK…", 0, "Preparing mod output.");

        try
        {
            // Build into the same virtual PAK store used by the Mod Manager.
            // Installed PAK mods live under:
            //   <ModsRoot>\\PAK\\<Mod Name>\\<Mod Name>_p.pak
            // and are enabled into the game's Content\\Paks\\~mods folder
            // through the Mod Manager's managed link system.
            var pakRoot = GetPakVirtualRoot();
            var modDirectory = Path.Combine(
                pakRoot,
                SanitizePakFolderName(modName));
            Directory.CreateDirectory(modDirectory);

            var outputPak = Path.Combine(
                modDirectory,
                modName + "_p.pak");

            var replacements = _assetWorkshopReplacements
                .Where(kvp =>
                    _assetWorkshopCustomImages.Contains(kvp.Key) &&
                    !string.IsNullOrWhiteSpace(kvp.Value) &&
                    File.Exists(kvp.Value))
                .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value))
                .ToList();

            await Task.Run(() =>
                BuildAssetWorkshopTexturePak(
                    _assetWorkshopSelectedPak!,
                    replacements,
                    outputPak,
                    (status, percent, detail) =>
                        SetOperationBusy(true, status, percent, detail)));

            // Register the new PAK exactly like an installed Mod Manager PAK:
            // metadata/manifest live beside the active PAK in the virtual store,
            // and the Mod Manager creates the managed ~mods link.
            var metadata = LoadNexusMetadata();
            var installedMeta = new NexusModMetadata(
                modName,
                "retrorewindvideostoresimulator",
                0,
                0,
                Path.GetFileName(outputPak))
            {
                DisplayName = modName,
                InstalledVersion = "Asset Workshop",
                LatestVersion = "Asset Workshop",
                Description = "Asset Workshop texture replacement mod.",
                DownloadedAtUtc = DateTime.UtcNow
            };

            metadata[PakMetadataKey(outputPak)] = installedMeta;
            WriteActivePakManifest(
                outputPak,
                installedMeta,
                modName,
                "Asset Workshop",
                Path.GetFileName(outputPak));
            SaveNexusMetadata(metadata);

            // Enable the built source through the same managed PAK link mechanism
            // used by installed mods. This does not open the mod folder.
            SetPakPathEnabled(
                GetVerifiedGameRoot(),
                outputPak,
                true);

            // The mod is ALWAYS installed in the normal PAK Mods structure.
            // The Nexus toggle only creates an additional ZIP copy.
            if (packForNexus)
            {
                var outputDirectory = GetAssetWorkshopNexusOutputDirectory();
                Directory.CreateDirectory(outputDirectory);

                var zipPath = Path.Combine(outputDirectory, modName + ".zip");
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                SetOperationBusy(true, "Packing mod for Nexus…", 100,
                    $"Creating {Path.GetFileName(zipPath)}");

                await Task.Run(() => ZipAssetWorkshopModDirectory(modDirectory, zipPath));

                AssetWorkshopStatus.Text =
                    $"Mod built and Nexus ZIP created: {zipPath}";

                OpenAssetWorkshopFolder(outputDirectory);
            }
            else
            {
                AssetWorkshopStatus.Text =
                    $"Mod built successfully: {modName}_p.pak";
            }

            SetOperationBusy(true, "Asset Workshop PAK built", 100,
                $"{modName}_p.pak");
        }
        catch (Exception ex)
        {
            AssetWorkshopStatus.Text = $"Build PAK failed: {ex.Message}";
            MessageBox.Show(this,
                $"The Asset Workshop PAK could not be built.\n\n{ex.Message}",
                "Build PAK",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetOperationBusy(false);
            UpdateAssetWorkshopBuildModeState();
        }
    }

    private static string SanitizeAssetWorkshopModName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var value = name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        value = string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Trim().TrimEnd('.', ' ');
    }

    private string GetAssetWorkshopModsRoot()
    {
        if (!string.IsNullOrWhiteSpace(ModsRoot))
            return ModsRoot;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "Retro Rewind Modhub", "Mods");
    }

    private static string GetAssetWorkshopNexusOutputDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "Retro Rewind Modhub", "Output");
    }

    private static void ZipAssetWorkshopModDirectory(string modDirectory, string zipPath)
    {
        if (!Directory.Exists(modDirectory))
            throw new DirectoryNotFoundException(modDirectory);

        var parent = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        if (File.Exists(zipPath))
            File.Delete(zipPath);

        // The PAK itself and its companion manifest are placed at the ZIP root,
        // matching the archive layout consumed by InstallModZipAsync.
        using var archive = System.IO.Compression.ZipFile.Open(
            zipPath,
            System.IO.Compression.ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(
                     modDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var entryName = Path.GetFileName(file);
            var archiveEntry = archive.CreateEntry(
                entryName,
                System.IO.Compression.CompressionLevel.Optimal);

            using var sourceStream = File.OpenRead(file);
            using var targetStream = archiveEntry.Open();
            sourceStream.CopyTo(targetStream);
        }
    }

    private static void OpenAssetWorkshopFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static string BuildAssetWorkshopTexturePak(
        string sourcePak,
        IReadOnlyList<KeyValuePair<string, string>> replacements,
        string outputPak,
        Action<string, double?, string?>? progress = null)
    {
        if (replacements.Count == 0)
            throw new InvalidOperationException("There are no custom texture replacements to package.");

        var repak = FindRepakExecutable();
        if (string.IsNullOrWhiteSpace(repak))
            throw new InvalidOperationException(
                "repak.exe was not found. Place it in Documents\\Retro Rewind Modhub\\Tools\\repak.exe.");


        if (!File.Exists(sourcePak))
            throw new FileNotFoundException("The vanilla Retro Rewind-Windows.pak could not be found.", sourcePak);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPak) ?? AppContext.BaseDirectory);

        var workRoot = Path.Combine(
            Path.GetTempPath(),
            "RetroRewindModHub",
            "AssetWorkshop",
            "pak_build_" + Guid.NewGuid().ToString("N"));

        var stageRoot = Path.Combine(workRoot, "stage");
        Directory.CreateDirectory(stageRoot);

        try
        {
            EnsureAssetWorkshopOodleForRepak(sourcePak, repak);

            var total = replacements.Count;
            var completed = 0;

            foreach (var replacement in replacements)
            {
                var assetPath = replacement.Key.Replace('\\', '/').TrimStart('/');
                if (assetPath.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                    assetPath = assetPath[..^7];

                var imagePath = replacement.Value;
                var extension = Path.GetExtension(imagePath);

                // The independent Retro Rewind injector decodes ordinary editable
                // image formats itself. Do not put the image file into the PAK: it
                // is encoded into the original cooked Texture2D bulk payload first.
                var supportedImage =
                    extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);

                if (!supportedImage)
                {
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(imagePath)} is not a supported editable image. " +
                        "Use PNG, JPG, JPEG, BMP, TIF, or TIFF for this injector version.");
                }

                var leaf = Path.GetFileName(assetPath);
                var percent = completed * 100.0 / total;

                progress?.Invoke(
                    $"Preparing {leaf}…",
                    percent,
                    $"{completed} / {total} textures complete");

                // Materialize the ORIGINAL cooked Texture2D and all available sidecars.
                // The injector then rebuilds the cooked texture using the selected image.
                var materialized = MaterializeTextureWithRepak(sourcePak, assetPath);

                try
                {
                    var uasset = FindMaterializedUasset(materialized, assetPath);
                    if (string.IsNullOrWhiteSpace(uasset) || !File.Exists(uasset))
                        throw new InvalidOperationException(
                            $"Could not materialize the original cooked texture package for {leaf}.");

                    var injectedRoot = Path.Combine(materialized, "injected");
                    Directory.CreateDirectory(injectedRoot);

                    progress?.Invoke(
                        $"Injecting {leaf}…",
                        Math.Min(95, percent + (90.0 / total)),
                        $"{completed + 1} / {total} • Encoding replacement image");

                    var injectedUasset = RunAssetWorkshopTextureInjectorImport(
                        uasset,
                        imagePath,
                        injectedRoot,
                        out var injectError);

                    if (string.IsNullOrWhiteSpace(injectedUasset) || !File.Exists(injectedUasset))
                    {
                        throw new InvalidOperationException(
                            string.IsNullOrWhiteSpace(injectError)
                                ? $"The texture injector did not produce an injected package for {leaf}."
                                : injectError);
                    }

                    var relativeDirectory = Path.GetDirectoryName(
                        assetPath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;

                    var stageDirectory = Path.Combine(
                        stageRoot,
                        relativeDirectory);

                    Directory.CreateDirectory(stageDirectory);

                    // The injector writes the rebuilt cooked package beside its output.
                    // Copy every generated sidecar that belongs to this asset.
                    var copied = 0;
                    foreach (var file in Directory.EnumerateFiles(
                                 injectedRoot,
                                 leaf + ".*",
                                 SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(file);
                        if (!ext.Equals(".uasset", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".uexp", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".ubulk", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".uptnl", StringComparison.OrdinalIgnoreCase))
                            continue;

                        File.Copy(
                            file,
                            Path.Combine(stageDirectory, Path.GetFileName(file)),
                            true);
                        copied++;
                    }

                    if (copied == 0)
                        throw new InvalidOperationException(
                            $"The injector completed for {leaf}, but produced no cooked asset sidecars.");

                    completed++;

                    progress?.Invoke(
                        $"Prepared {leaf}",
                        completed * 100.0 / total,
                        $"{completed} / {total} textures ready for packaging");
                }
                finally
                {
                    try { Directory.Delete(materialized, true); } catch { }
                }
            }

            if (!Directory.EnumerateFiles(
                    stageRoot,
                    "*",
                    SearchOption.AllDirectories).Any())
            {
                throw new InvalidOperationException("No cooked replacement assets were staged.");
            }

            progress?.Invoke(
                "Packaging Unreal PAK…",
                98,
                $"Packing {completed} cooked texture replacement(s)");

            var packResult = RunAssetWorkshopRepakPack(
                repak,
                stageRoot,
                outputPak,
                out var packError);

            if (!packResult)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(packError)
                        ? "repak failed while creating the PAK."
                        : packError);

            if (!File.Exists(outputPak) || new FileInfo(outputPak).Length == 0)
                throw new InvalidOperationException(
                    "repak reported success, but the output PAK was not created.");

            progress?.Invoke(
                "PAK build complete",
                100,
                $"{Path.GetFileName(outputPak)} • {new FileInfo(outputPak).Length:N0} bytes");

            return outputPak;
        }
        finally
        {
            try { Directory.Delete(workRoot, true); } catch { }
        }
    }

    private static string? RunAssetWorkshopTextureInjectorImport(
        string uassetPath,
        string imagePath,
        string outputDir,
        out string error)
    {
        error = "";
        try
        {
            Directory.CreateDirectory(outputDir);
            return ExternalTextureInjectorBridge.Replace(uassetPath, imagePath, outputDir);
        }
        catch (Exception ex)
        {
            error = "Independent Retro Rewind texture injector failed: " + ex.Message;
            return null;
        }
    }

    private static bool RunAssetWorkshopRepakPack(
        string repak,
        string stageRoot,
        string outputPak,
        out string error)
    {
        error = "";

        var psi = new ProcessStartInfo
        {
            FileName = repak,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(repak) ?? AppContext.BaseDirectory
        };

        // Match the documented Retro Rewind packaging settings:
        // UE5 PAK v11, ../../../ mount point, and the Retro Rewind base path hash seed.
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add("--version");
        psi.ArgumentList.Add("V11");
        psi.ArgumentList.Add("--mount-point");
        psi.ArgumentList.Add("../../../");
        psi.ArgumentList.Add("--path-hash-seed");
        // Use the Retro Rewind PAK path-hash seed required by the game (0xC04CF817).
        psi.ArgumentList.Add("3226269719");

        psi.ArgumentList.Add(stageRoot);
        psi.ArgumentList.Add(outputPak);

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                error = "Could not start repak.";
                return false;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                error =
                    $"repak failed (exit code {process.ExitCode}).\n" +
                    $"{stderr.Trim()}\n{stdout.Trim()}".Trim();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static async Task<BitmapImage> LoadAssetWorkshopBitmapAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void AssetWorkshopTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Legacy hidden Asset Workshop control. Category navigation now uses
        // AssetWorkshopCategory_Click and opens a dedicated page.
    }


    private static string GetAssetWorkshopCategoryDescription(string type) =>
        type switch
        {
            "Texture" => "Textures\nBrowse the game's T_*_bc base-colour textures. Preview them, choose replacement images, save/load texture projects, and build a replacement PAK.",
            "Static Mesh" => "Static Meshes\nBrowse the game's LA_* and SM_* static meshes. Preview them and export their .uasset/.uexp files for further modding work.",
            "Skeletal Mesh" => "Skeletal Meshes\nBrowse skeletal mesh assets and export their .uasset and .uexp files for further work. Build Mode replacement features are not available for this category.",
            "Material" => "Materials\nBrowse material assets and export their .uasset and .uexp files for further modding work.",
            "Animation" => "Animations\nBrowse animation assets and export their .uasset and .uexp files for further editing or modding.",
            "Audio" => "Audio\nBrowse audio assets and export their .uasset and .uexp files for further editing or modding.",
            "Blueprint" => "Blueprints\nBrowse Blueprint assets and export their .uasset and .uexp files for further modding work.",
            "Niagara" => "Niagara\nBrowse Niagara assets and export their .uasset and .uexp files for further modding work.",
            "Particle" => "Particles\nBrowse particle assets and export their .uasset and .uexp files for further editing or modding.",
            "Widget" => "Widgets\nBrowse UI widget assets and export their .uasset and .uexp files for further editing or modding.",
            "World" => "Worlds\nBrowse world assets and export their .uasset and .uexp files for further work.",
            "Other" => "Other\nBrowse other supported asset types and export their .uasset and .uexp files.",
            _ => "Select an Asset Workshop category to see what you can do with it."
        };

    private void AssetWorkshopCategory_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button button && button.Tag is string type &&
            AssetWorkshopCategoryDescription != null)
        {
            AssetWorkshopCategoryDescription.Text =
                GetAssetWorkshopCategoryDescription(type);
        }
    }

    private void AssetWorkshopCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string type) return;
        if (!AssetWorkshopTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) return;

        // Build Mode is page-specific. Always leave it disabled when switching pages,
        // so non-build asset categories never inherit the previous page's Build Mode state.
        _assetWorkshopBuildMode = false;
        _assetWorkshopSelectedTexturePath = null;
        _assetWorkshopLoadedProjectName = null;
        _assetWorkshopActiveType = type;
        SetAssetWorkshopCategoryButtonState();

        _mode = type switch
        {
            "Texture" => "asset_texture",
            "Static Mesh" => "asset_staticmesh",
            "Skeletal Mesh" => "asset_skeletalmesh",
            "Material" => "asset_material",
            "Animation" => "asset_animation",
            "Audio" => "asset_audio",
            "Blueprint" => "asset_blueprint",
            "Niagara" => "asset_niagara",
            "Particle" => "asset_particle",
            "Widget" => "asset_widget",
            "World" => "asset_world",
            "Other" => "asset_other",
            _ => "assets"
        };

        UpdateAssetWorkshopSharedPageHeader();
        UpdateMode();


        if (IsAssetWorkshopCategoryMode(_mode))
            _ = LoadAssetWorkshopPaksAsync();
    }

    private void UpdateAssetWorkshopSharedPageHeader()
    {
        if (AssetWorkshopPageTitle != null)
            AssetWorkshopPageTitle.Text = GetAssetWorkshopDisplayType(_assetWorkshopActiveType);
        if (AssetWorkshopPageSubtitle != null)
        {
            AssetWorkshopPageSubtitle.Text = _assetWorkshopActiveType switch
            {
                "Texture" => "Vanilla game assets · T_*_bc base-colour textures only",
                "Static Mesh" => "Vanilla game assets · LA_* and SM_* static meshes only",
                _ => "Vanilla game assets · export .uasset/.uexp"
            };
        }

        var fullAssetPage = IsAssetWorkshopFullPageType(_assetWorkshopActiveType);
        var buildAssetPage = string.Equals(_assetWorkshopActiveType, "Texture", StringComparison.OrdinalIgnoreCase);
        if (AssetWorkshopBuildModeToggle != null)
        {
            AssetWorkshopBuildModeToggle.Visibility = buildAssetPage ? Visibility.Visible : Visibility.Collapsed;
            AssetWorkshopBuildModeToggle.IsChecked = buildAssetPage && _assetWorkshopBuildMode;
        }
        if (AssetWorkshopBuildPakButton != null)
            AssetWorkshopBuildPakButton.Visibility =
                buildAssetPage && _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
        if (AssetWorkshopBuildPreviewPanel != null)
            AssetWorkshopBuildPreviewPanel.Visibility =
                buildAssetPage && _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
        if (AssetWorkshopBuildSaveButton != null)
            AssetWorkshopBuildSaveButton.Visibility =
                buildAssetPage && _assetWorkshopBuildMode ? Visibility.Visible : Visibility.Collapsed;
        if (AssetWorkshopBuildLoadButton != null)
        {
            AssetWorkshopBuildLoadButton.Visibility =
                buildAssetPage ? Visibility.Visible : Visibility.Collapsed;
            AssetWorkshopBuildLoadButton.IsEnabled =
                buildAssetPage && HasValidAssetWorkshopProjects();
        }
        if (AssetWorkshopExtractButton != null)
            AssetWorkshopExtractButton.Content = fullAssetPage ? "Extract Asset" : "Export .uasset/.uexp";
    }

    private string? FindVanillaRetroRewindPak(string gameRoot)
    {
        var pakRoot = GetPakRootForAssetWorkshop(gameRoot);
        if (!Directory.Exists(pakRoot)) return null;

        return Directory.EnumerateFiles(pakRoot, "RetroRewind-Windows.pak", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private string GetPakRootForAssetWorkshop(string gameRoot)
    {
        // Retro Rewind's shipped Unreal project is normally nested as:
        //   <SteamLibrary>\steamapps\common\RetroRewind\RetroRewind\Content\Paks
        // Steam verification may return either the outer install directory or
        // an already-resolved project directory, so explicitly test both.
        var fullRoot = Path.GetFullPath(gameRoot);
        var candidates = new List<string>
        {
            Path.Combine(fullRoot, "RetroRewind", "Content", "Paks"),
            Path.Combine(fullRoot, "Content", "Paks")
        };

        // If the supplied root is the Steam library/game install parent, also
        // resolve the known Retro Rewind project layout without relying on
        // the generic project-root heuristic.
        var normalized = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalized.EndsWith(Path.Combine("steamapps", "common", "RetroRewind"), StringComparison.OrdinalIgnoreCase))
        {
            candidates.Insert(0, Path.Combine(fullRoot, "RetroRewind", "Content", "Paks"));
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(candidate)) return candidate;
        }

        // Preserve the existing generic detection as a final fallback for
        // other supported layouts.
        return Path.Combine(GetGameProjectRoot(gameRoot), "Content", "Paks");
    }

    private static string GetAssetPakDisplayName(string pakPath, string pakRoot)
    {
        var relative = Path.GetRelativePath(pakRoot, pakPath).Replace('\\', '/');
        return relative;
    }

    private async Task LoadAssetWorkshopEntriesAsync(string pakPath)
    {
        try
        {
            _assetWorkshopEntries = new List<AssetEntryItem>();
            AssetWorkshopEntriesList.Items.Clear();
            AssetWorkshopEntryCount.Text = "";
            AssetWorkshopSearchBox.Text = "";
            AssetWorkshopExtractButton.IsEnabled = false;
            AssetWorkshopStatus.Text = $"Indexing {_assetWorkshopPaks.FirstOrDefault(p => p.Path.Equals(pakPath, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? Path.GetFileName(pakPath)}…";

            var result = await Task.Run(() => ReadAssetWorkshopEntries(pakPath, _assetWorkshopActiveType));
            if (!result.Success)
            {
                AssetWorkshopStatus.Text = result.Error;
                return;
            }

            _assetWorkshopEntries = result.Entries;
            RenderAssetWorkshopEntries();
            AssetWorkshopStatus.Text = result.Encrypted
                ? "The PAK index is encrypted. Its contents cannot be browsed without the game's AES key."
                : $"Loaded {_assetWorkshopEntries.Count:N0} logical asset(s).";
        }
        catch (Exception ex)
        {
            AssetWorkshopStatus.Text = $"Could not read PAK: {ex.Message}";
        }
    }

    private sealed record AssetReadResult(bool Success, List<AssetEntryItem> Entries, bool Encrypted, string Error);

    private AssetReadResult ReadAssetWorkshopEntries(string pakPath, string assetType)
    {
        try
        {
            if (!TryReadPakWithBuiltInReader(pakPath, out var files, out _, out var error))
                return new AssetReadResult(false, new List<AssetEntryItem>(), false, string.IsNullOrWhiteSpace(error) ? "The PAK could not be opened." : error);

            // Unreal assets are commonly stored as a group of files:
            //   Foo.uasset + Foo.uexp + optional Foo.ubulk/Foo.m.ubulk...
            // Show the logical asset once instead of exposing every companion file.
            // A .umap is treated as a primary asset as well. Other files remain hidden
            // from the asset browser for now; later phases can expose them as dependencies.
            var normalized = files
                .Select(path => path.Replace('\\', '/'))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var primaryAssets = normalized
                .Where(IsPrimaryUnrealAsset)
                .Where(path => IsAssetWorkshopPathAllowed(path, assetType))
                .Where(path => InferAssetIdentityFromPath(path).Type.Equals(assetType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // IMPORTANT: indexing must stay cheap. Do not create a CUE4Parse provider
            // and parse every package here. A vanilla PAK can contain thousands of
            // logical assets, and doing a full Unreal package load (plus loose-file
            // fallback) for every entry makes the UI appear frozen for a very long
            // time. We classify the list using the fast Unreal naming/path convention
            // and defer CUE4Parse parsing until the user selects an asset.
            var entries = new List<AssetEntryItem>(primaryAssets.Count);
            using var stream = File.OpenRead(pakPath);
            var reader = CreateAssetPakReader(stream);

            foreach (var assetPath in primaryAssets)
            {
                var companions = FindAssetCompanionFiles(assetPath, normalized);
                var assetFiles = new[] { assetPath }.Concat(companions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                long totalSize = 0;
                long totalCompressed = 0;
                bool hasSize = false;
                bool hasCompressed = false;
                bool encrypted = false;
                string compression = "";

                foreach (var filePath in assetFiles)
                {
                    try
                    {
                        var info = GetReaderEntryInfo(reader, filePath);
                        if (info == null) continue;
                        var size = ReadLongMember(info, "UncompressedSize");
                        var compressed = ReadLongMember(info, "CompressedSize");
                        if (size.HasValue) { totalSize += size.Value; hasSize = true; }
                        if (compressed.HasValue) { totalCompressed += compressed.Value; hasCompressed = true; }
                        encrypted |= ReadBoolMember(info, "IsEncrypted");
                        if (string.IsNullOrWhiteSpace(compression)) compression = AssetWorkshopGetReflectedString(info, "Compression");
                    }
                    catch
                    {
                        // One companion can be unavailable while the primary asset remains browsable.
                    }
                }

                // Fast classification for the browser. Exact Unreal class detection
                // is intentionally deferred until selection/preview so scanning the
                // entire vanilla PAK remains responsive.
                var identity = InferAssetIdentityFromPath(assetPath);
                var assetName = identity.Name;
                var inferredAssetType = identity.Type;

                entries.Add(new AssetEntryItem(
                    assetPath,
                    assetName,
                    inferredAssetType,
                    hasSize ? totalSize : null,
                    hasCompressed ? totalCompressed : null,
                    compression,
                    encrypted,
                    assetFiles.Count,
                    identity.SourceClass));
            }

            return new AssetReadResult(true, entries, false, "");
        }
        catch (Exception ex)
        {
            return new AssetReadResult(false, new List<AssetEntryItem>(), false, ex.Message);
        }
    }

    private sealed record UnrealAssetIdentity(string Name, string Type, string SourceClass);

    private static UnrealAssetIdentity TryIdentifyUnrealAssetWithCUE4Parse(DefaultFileProvider provider, string pakPath, string assetPath)
    {
        // First try the normal mounted-PAK path. CUE4Parse documents loading a
        // package from the resolved GameFile, which is preferable when the archive
        // can be mounted normally.
        try
        {
            if (TryLoadAssetWorkshopPackage(provider, assetPath, out var package) && package != null)
            {
                var identity = IdentifyAssetFromCuePackage(package, assetPath);
                if (!identity.Type.Equals("Other", StringComparison.OrdinalIgnoreCase))
                    return identity;
            }
        }
        catch
        {
            // Fall through to the loose-package path below.
        }

        // Before doing a potentially expensive loose-package parse, use Retro
        // Rewind's documented cooked-asset naming conventions. The current
        // documentation identifies T_* base-colour textures and LA_*/SM_* meshes
        // from the vanilla PAK, so these are useful classification fallbacks when
        // the package payload itself cannot be opened.
        var inferredIdentity = InferAssetIdentityFromPath(assetPath);
        if (!inferredIdentity.Type.Equals("Other", StringComparison.OrdinalIgnoreCase))
            return inferredIdentity;

        // Retro Rewind's cooked PAK can expose its index while still preventing
        // CUE4Parse from reading a package directly from the mounted archive. In
        // that situation, use the same PAK reader already used by the browser to
        // materialize only this package and its Unreal sidecars, then let CUE4Parse
        // parse the loose cooked package. This also guarantees that .uexp/.ubulk
        // payloads are available to Texture2D.Decode().
        try
        {
            if (TryIdentifyAssetFromLoosePackage(pakPath, assetPath, out var looseIdentity))
                return looseIdentity;
        }
        catch
        {
            // Keep the UI usable even when one package is malformed or unsupported.
        }

        return InferAssetIdentityFromPath(assetPath);
    }

    private static UnrealAssetIdentity IdentifyAssetFromCuePackage(IPackage package, string assetPath)
    {
        string bestName = Path.GetFileNameWithoutExtension(assetPath);
        string bestClass = "";

        foreach (var export in package.GetExports())
        {
            if (export == null) continue;
            var className = export.GetType().Name;
            var objectName = AssetWorkshopGetReflectedString(export, "Name");
            if (string.IsNullOrWhiteSpace(objectName))
                objectName = AssetWorkshopGetReflectedString(export, "ObjectName");

            if (!string.IsNullOrWhiteSpace(objectName)
                && (string.IsNullOrWhiteSpace(bestClass) || IsRecognizedAssetClass(className)))
            {
                bestName = objectName;
                bestClass = className;
            }

            if (IsRecognizedAssetClass(className))
                break;
        }

        var mappedType = MapUnrealClassToDisplayType(bestClass);
        if (mappedType.Equals("Other", StringComparison.OrdinalIgnoreCase))
        {
            var inferred = InferAssetIdentityFromPath(assetPath);
            return new UnrealAssetIdentity(
                string.IsNullOrWhiteSpace(bestName) ? inferred.Name : bestName,
                inferred.Type,
                bestClass);
        }

        return new UnrealAssetIdentity(bestName, mappedType, bestClass);
    }

    private static bool TryIdentifyAssetFromLoosePackage(string pakPath, string assetPath, out UnrealAssetIdentity identity)
    {
        identity = default!;
        if (!TryLoadAssetWorkshopPackageFromLooseFiles(pakPath, assetPath, out var package, out var tempRoot) || package == null)
            return false;

        try
        {
            identity = IdentifyAssetFromCuePackage(package, assetPath);
            return true;
        }
        finally
        {
            try { if (!string.IsNullOrWhiteSpace(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static UnrealAssetIdentity InferAssetIdentityFromPath(string assetPath)
    {
        var name = Path.GetFileNameWithoutExtension(assetPath);
        var normalized = assetPath.Replace('\\', '/');
        var upperName = name.ToUpperInvariant();
        var upperPath = normalized.ToUpperInvariant();

        if (normalized.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            return new UnrealAssetIdentity(name, "World", "ULevel");

        // Retro Rewind follows the conventional Unreal naming scheme. These are
        // deliberately fallback classifications only; a successfully loaded CUE4Parse
        // export always takes precedence.
        if (IsAssetWorkshopTexturePath(assetPath))
            return new UnrealAssetIdentity(name, "Texture", "Texture2D");
        if (IsAssetWorkshopStaticMeshPath(assetPath))
            return new UnrealAssetIdentity(name, "Static Mesh", "StaticMesh");
        if (upperName.StartsWith("SK_") || upperName.StartsWith("SKEL_") || upperPath.Contains("/SKELETALMESH/") || upperPath.Contains("/SKELETALMESHES/"))
            return new UnrealAssetIdentity(name, "Skeletal Mesh", "SkeletalMesh");
        if (upperName.StartsWith("MI_") || upperName.StartsWith("M_") || upperName.StartsWith("MAT_") || upperPath.Contains("/MATERIALS/") || upperPath.Contains("/MATERIAL/"))
            return new UnrealAssetIdentity(name, "Material", "Material");
        if (upperName.StartsWith("A_") || upperName.StartsWith("AN_") || upperName.StartsWith("ABP_") || upperName.StartsWith("AM_") || upperPath.Contains("/ANIMATIONS/") || upperPath.Contains("/ANIMATION/"))
            return new UnrealAssetIdentity(name, "Animation", "AnimSequence");
        if (upperName.StartsWith("NS_") || upperName.StartsWith("FX_") || upperName.StartsWith("VFX_") || upperPath.Contains("/NIAGARA/"))
            return new UnrealAssetIdentity(name, "Niagara", "Niagara");
        if (upperName.StartsWith("W_") || upperName.StartsWith("WBP_") || upperPath.Contains("/WIDGETS/"))
            return new UnrealAssetIdentity(name, "Widget", "Widget");
        if (upperName.StartsWith("BP_") || upperName.StartsWith("BPI_") || upperName.StartsWith("BPC_") || upperPath.Contains("/BLUEPRINTS/"))
            return new UnrealAssetIdentity(name, "Blueprint", "Blueprint");
        if (upperName.StartsWith("S_") || upperName.StartsWith("SFX_") || upperName.StartsWith("VO_") || upperPath.Contains("/SOUND/") || upperPath.Contains("/SOUNDS/") || upperPath.Contains("/AUDIO/") || upperPath.Contains("/WWISE/"))
            return new UnrealAssetIdentity(name, "Audio", "Sound");

        return new UnrealAssetIdentity(name, "Other", "");
    }

    private static string NormalizeAssetWorkshopCuePath(string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        var contentMarker = "/Content/";
        var contentIndex = normalized.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        string package;
        if (contentIndex >= 0)
            package = "/Game/" + normalized[(contentIndex + contentMarker.Length)..];
        else if (normalized.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            package = "/Game/" + normalized["Content/".Length..];
        else if (normalized.StartsWith("Game/", StringComparison.OrdinalIgnoreCase))
            package = "/" + normalized;
        else
            package = "/Game/" + normalized;

        if (package.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) || package.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            package = package[..package.LastIndexOf('.')];
        return package;
    }

    private static UnrealAssetIdentity TryIdentifyUnrealAsset(object pakReader, Stream pakStream, string assetPath)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "UAssetAPI", StringComparison.OrdinalIgnoreCase))
                ?? Assembly.Load("UAssetAPI");
            var uassetType = assembly.GetType("UAssetAPI.UAsset", throwOnError: true)!;
            var tempRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "AssetWorkshop", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                var safeName = Path.GetFileName(assetPath);
                var uassetPath = Path.Combine(tempRoot, safeName);
                ExtractAssetEntry(pakReader, pakStream, assetPath, uassetPath);

                var companion = Path.ChangeExtension(assetPath, ".uexp");
                var files = GetReaderFiles(pakReader);
                if (files.Any(f => string.Equals(f, companion, StringComparison.OrdinalIgnoreCase)))
                {
                    ExtractAssetEntry(pakReader, pakStream, companion, Path.ChangeExtension(uassetPath, ".uexp"));
                }

                var ctor = uassetType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(c =>
                    {
                        var ps = c.GetParameters();
                        return ps.Length == 6 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(bool);
                    });
                if (ctor == null) return new UnrealAssetIdentity("", "", "");

                var parameters = ctor.GetParameters();
                var args = new object?[6];
                args[0] = uassetPath;
                args[1] = true;
                for (var i = 2; i < parameters.Length; i++)
                    args[i] = parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null;

                var asset = ctor.Invoke(args);
                var exports = AssetWorkshopGetReflectedMember(asset, "Exports") as System.Collections.IEnumerable;
                if (exports == null) return new UnrealAssetIdentity(Path.GetFileNameWithoutExtension(assetPath), "", "");

                string bestName = "";
                string bestClass = "";
                foreach (var export in exports.Cast<object>())
                {
                    // UAssetAPI exposes the resolved UObject class directly on Export.
                    // Prefer GetExportClassType() over manually resolving ClassIndex; the
                    // latter can return an import/object name that is not the actual class.
                    var className = GetExportClassTypeName(asset, export);
                    if (string.IsNullOrWhiteSpace(className))
                        className = ResolveExportClassName(asset, export);
                    var objectName = GetFNameText(AssetWorkshopGetReflectedMember(export, "ObjectName"));
                    if (string.IsNullOrWhiteSpace(objectName)) continue;
                    if (string.IsNullOrWhiteSpace(bestName))
                    {
                        bestName = objectName;
                        bestClass = className;
                    }
                    if (ReadBoolMember(export, "bIsAsset") || IsRecognizedAssetClass(className))
                    {
                        bestName = objectName;
                        bestClass = className;
                        break;
                    }
                }

                return new UnrealAssetIdentity(
                    string.IsNullOrWhiteSpace(bestName) ? Path.GetFileNameWithoutExtension(assetPath) : bestName,
                    MapUnrealClassToDisplayType(bestClass),
                    bestClass);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
        catch
        {
            return new UnrealAssetIdentity(Path.GetFileNameWithoutExtension(assetPath), "", "");
        }
    }

    private static string GetExportClassTypeName(object asset, object export)
    {
        try
        {
            var method = export.GetType().GetMethod("GetExportClassType", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method == null) return "";
            var value = method.Invoke(export, null);
            return GetFNameText(value);
        }
        catch
        {
            return "";
        }
    }

    private static string ResolveExportClassName(object asset, object export)
    {
        var classIndex = AssetWorkshopGetReflectedMember(export, "ClassIndex");
        var index = ReadIntMember(classIndex, "Index");
        if (!index.HasValue || index.Value == 0) return "";

        var collectionName = index.Value < 0 ? "Imports" : "Exports";
        var collection = AssetWorkshopGetReflectedMember(asset, collectionName) as System.Collections.IList;
        if (collection == null) return "";
        var position = index.Value < 0 ? -index.Value - 1 : index.Value - 1;
        if (position < 0 || position >= collection.Count) return "";
        return GetFNameText(AssetWorkshopGetReflectedMember(collection[position]!, "ObjectName"));
    }

    private static int? ReadIntMember(object? instance, string name)
    {
        if (instance == null) return null;
        var value = AssetWorkshopGetReflectedMember(instance, name);
        return value switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            uint u when u <= int.MaxValue => (int)u,
            _ => int.TryParse(value?.ToString(), out var parsed) ? parsed : null
        };
    }

    private static string GetFNameText(object? value)
    {
        if (value == null) return "";
        var direct = value.ToString();
        if (!string.IsNullOrWhiteSpace(direct) && !direct.Contains("UAssetAPI", StringComparison.OrdinalIgnoreCase)) return direct;
        var nested = AssetWorkshopGetReflectedMember(value, "Value");
        return nested?.ToString() ?? direct ?? "";
    }

    private static bool IsRecognizedAssetClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return false;
        return className.Contains("Texture", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Mesh", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Material", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Blueprint", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Sound", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Anim", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Niagara", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Particle", StringComparison.OrdinalIgnoreCase)
            || className.Contains("World", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Widget", StringComparison.OrdinalIgnoreCase);
    }

    private static string MapUnrealClassToDisplayType(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return "Other";
        var name = className.Trim();
        if (name.Equals("Texture2D", StringComparison.OrdinalIgnoreCase) || name.Contains("Texture", StringComparison.OrdinalIgnoreCase)) return "Texture";
        if (name.Contains("StaticMesh", StringComparison.OrdinalIgnoreCase)) return "Static Mesh";
        if (name.Contains("SkeletalMesh", StringComparison.OrdinalIgnoreCase)) return "Skeletal Mesh";
        if (name.Contains("Material", StringComparison.OrdinalIgnoreCase)) return "Material";
        if (name.Contains("Blueprint", StringComparison.OrdinalIgnoreCase)) return "Blueprint";
        if (name.Contains("Anim", StringComparison.OrdinalIgnoreCase)) return "Animation";
        if (name.Contains("Sound", StringComparison.OrdinalIgnoreCase) || name.Contains("Audio", StringComparison.OrdinalIgnoreCase) || name.Contains("Wwise", StringComparison.OrdinalIgnoreCase)) return "Audio";
        if (name.Contains("Niagara", StringComparison.OrdinalIgnoreCase)) return "Niagara";
        if (name.Contains("Particle", StringComparison.OrdinalIgnoreCase)) return "Particle";
        if (name.Contains("Widget", StringComparison.OrdinalIgnoreCase)) return "Widget";
        if (name.Contains("World", StringComparison.OrdinalIgnoreCase) || name.Equals("ULevel", StringComparison.OrdinalIgnoreCase)) return "World";
        return "Other";
    }

    private static bool IsPrimaryUnrealAsset(string path)
    {
        return path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> FindAssetCompanionFiles(string assetPath, List<string> allFiles)
    {
        var requested = GetContentRelativeAssetPath(assetPath);
        var directory = Path.GetDirectoryName(requested)?.Replace('\\', '/') ?? "";
        var fileName = Path.GetFileNameWithoutExtension(requested);
        var prefix = string.IsNullOrEmpty(directory) ? fileName : directory.TrimEnd('/') + "/" + fileName;

        return allFiles
            .Where(path =>
            {
                var candidate = GetContentRelativeAssetPath(path);
                return candidate.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
                    && (candidate.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase)
                        || candidate.EndsWith(".ubulk", StringComparison.OrdinalIgnoreCase)
                        || candidate.EndsWith(".uptnl", StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    private static void EnsureAssetWorkshopUnpakerOodle(string selectedPak)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(selectedPak) || !File.Exists(selectedPak))
                return;

            var paksDirectory = Directory.GetParent(selectedPak)?.FullName;
            var contentDirectory = Directory.GetParent(paksDirectory ?? string.Empty)?.FullName;
            var gameRoot = Directory.GetParent(contentDirectory ?? string.Empty)?.FullName;
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                return;

            var candidates = new[]
            {
                Path.Combine(gameRoot, "Binaries", "Win64", "oo2core_9_win64.dll"),
                Path.Combine(gameRoot, "Engine", "Binaries", "Win64", "oo2core_9_win64.dll"),
                Path.Combine(gameRoot, "ThirdParty", "Oodle", "Win64", "oo2core_9_win64.dll"),
                Path.Combine(gameRoot, "ThirdParty", "Oodle", "x64", "oo2core_9_win64.dll")
            };

            var source = candidates.FirstOrDefault(File.Exists);
            if (source == null)
            {
                // Some UE installations place Oodle in a nested ThirdParty folder.
                // Keep the fallback search bounded to the game directory and stop at
                // the first matching DLL; this runs only when the common paths miss.
                try
                {
                    source = Directory.EnumerateFiles(
                            gameRoot, "oo2core_9_win64.dll", SearchOption.AllDirectories)
                        .FirstOrDefault();
                }
                catch { }
            }

            if (source == null)
                return;

            // Unpaker 1.1.0/Oodle.NET expects oo2core_9_win64.dll beside the host
            // application. Copy the DLL from the user's own game installation rather
            // than bundling or downloading a third-party copy.
            var destination = Path.Combine(AppContext.BaseDirectory, "oo2core_9_win64.dll");
            if (!File.Exists(destination) || new FileInfo(destination).Length != new FileInfo(source).Length)
            {
                try
                {
                    File.Copy(source, destination, true);
                }
                catch
                {
                    // A locked native DLL may already be loaded. In that case leave it
                    // in place and let Unpaker use the already-loaded runtime.
                }
            }

            // Also expose the path for CUE4Parse's OodleHelper when it is initialized
            // later in the same process.
            try { Environment.SetEnvironmentVariable("OODLE_PATH", destination); } catch { }
        }
        catch
        {
            // The caller will surface Unpaker's actual Oodle error if no runtime is
            // available. Do not make unrelated Asset Workshop indexing fail here.
        }
    }

    private static object CreateAssetPakReader(Stream stream)
    {
        // CreateAssetPakReader is also used by older Asset Workshop paths. The
        // Oodle DLL must be present before Unpaker constructs its reader because
        // Oodle.NET resolves the native library from the application directory.
        // Callers that know the PAK path should call EnsureAssetWorkshopUnpakerOodle
        // first; this method deliberately does not guess a game installation.
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "Unpaker", StringComparison.OrdinalIgnoreCase))
            ?? Assembly.Load("Unpaker");
        var readerType = assembly.GetType("Unpaker.PakReader", throwOnError: true)!;
        var create = readerType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 2);
        if (create == null) throw new MissingMethodException("The bundled PAK reader does not expose its reader factory.");
        try
        {
            return create.Invoke(null, new object?[] { stream, null })
                ?? throw new InvalidOperationException("The bundled PAK reader could not open the archive.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException(
                $"The bundled PAK reader could not open the archive: {ex.InnerException.Message}",
                ex.InnerException);
        }
    }

    private static object? GetReaderEntryInfo(object reader, string path)
    {
        var method = reader.GetType().GetMethod("GetEntryInfo", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
        return method?.Invoke(reader, new object[] { path });
    }

    private static object? AssetWorkshopGetReflectedMember(object? instance, string name)
    {
        if (instance == null) return null;
        var type = instance.GetType();
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null) return field.GetValue(instance);
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property?.GetValue(instance);
    }

    private static string AssetWorkshopGetReflectedString(object? instance, string name)
    {
        return AssetWorkshopGetReflectedMember(instance, name)?.ToString() ?? "";
    }

    private static long? ReadLongMember(object instance, string name)
    {
        var value = AssetWorkshopGetReflectedMember(instance, name);
        return value switch
        {
            ulong u => u > long.MaxValue ? long.MaxValue : (long)u,
            uint u => u,
            long l => l,
            int i => i,
            _ => long.TryParse(value?.ToString(), out var parsed) ? parsed : null
        };
    }

    private static bool ReadBoolMember(object instance, string name)
    {
        var value = AssetWorkshopGetReflectedMember(instance, name);
        return value is bool b && b;
    }

    private void AssetWorkshopSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderAssetWorkshopEntries();
    }

    private void RenderAssetWorkshopEntries()
    {
        if (AssetWorkshopEntriesList == null) return;

        UpdateAssetWorkshopSharedPageHeader();
        var query = AssetWorkshopSearchBox?.Text?.Trim() ?? "";
        IEnumerable<AssetEntryItem> source = _assetWorkshopEntries.Where(e =>
            IsAssetWorkshopPathAllowed(e.Path, _assetWorkshopActiveType));

        if (string.Equals(_assetWorkshopActiveType, "Texture", StringComparison.OrdinalIgnoreCase) &&
            _assetWorkshopBuildMode)
            source = source.Where(e => _assetWorkshopReplacements.ContainsKey(e.Path));
        if (!string.IsNullOrWhiteSpace(query))
            source = source.Where(e => e.AssetName.Contains(query, StringComparison.OrdinalIgnoreCase));

        var sourceList = source.ToList();
        AssetWorkshopEntriesList.Items.Clear();
        if (IsAssetWorkshopFullPageType(_assetWorkshopActiveType))
        {
            var groups = GetAssetWorkshopAssetGroups(sourceList, _assetWorkshopActiveType).ToList();
            foreach (var group in groups)
                AssetWorkshopEntriesList.Items.Add(CreateAssetWorkshopTextureGroupRow(group));
            AssetWorkshopEntryCount.Text = $"{groups.Count:N0} groups";
        }
        else
        {
            foreach (var entry in sourceList.OrderBy(e => e.AssetName, StringComparer.OrdinalIgnoreCase))
                AssetWorkshopEntriesList.Items.Add(CreateAssetWorkshopSimpleAssetRow(entry));
            AssetWorkshopEntryCount.Text = $"{sourceList.Count:N0} assets";
        }
        UpdateAssetWorkshopTextureEntryHighlights();
        UpdateAssetWorkshopBuildModeState();
    }

    private void ClearAssetWorkshopPreview()
    {
        StopAssetWorkshopAudio();
        HideAssetWorkshopAudioControls();
        if (AssetWorkshopPreviewName != null) AssetWorkshopPreviewName.Text = "Select an asset";
        if (AssetWorkshopPreviewType != null) AssetWorkshopPreviewType.Text = "";
        if (AssetWorkshopPreviewSize != null) AssetWorkshopPreviewSize.Text = "";
        if (AssetWorkshopPreviewPath != null) AssetWorkshopPreviewPath.Text = "";
        if (AssetWorkshopPreviewSourceClass != null) AssetWorkshopPreviewSourceClass.Text = "";
        if (AssetWorkshopPreviewFiles != null) AssetWorkshopPreviewFiles.Text = "";
        if (AssetWorkshopPreviewMeshPreview != null) AssetWorkshopPreviewMeshPreview.Text = "";
        if (AssetWorkshopPreviewStatus != null)
        {
            AssetWorkshopPreviewStatus.Visibility = Visibility.Visible;
            AssetWorkshopPreviewStatus.Text = "Choose an asset from the list to view its information.";
        }
        if (AssetWorkshopPreviewImage != null)
        {
            AssetWorkshopPreviewImage.Source = null;
            AssetWorkshopPreviewImage.Visibility = Visibility.Collapsed;
        }
    }

    private async void AssetWorkshopEntriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AssetEntryItem? entry = null;
        AssetWorkshopTextureGroup? group = null;

        if (AssetWorkshopEntriesList.SelectedItem is Grid grid)
        {
            entry = grid.Tag as AssetEntryItem;
            group = grid.Tag as AssetWorkshopTextureGroup;
        }
        else if (AssetWorkshopEntriesList.SelectedItem is FrameworkElement element)
        {
            entry = element.Tag as AssetEntryItem;
            group = element.Tag as AssetWorkshopTextureGroup;
        }

        if (group != null)
        {
            var representative = group.Entries.FirstOrDefault(e =>
                string.Equals(e.Path, _assetWorkshopSelectedTexturePath, StringComparison.OrdinalIgnoreCase))
                ?? group.Entries.FirstOrDefault();

            if (representative != null)
                _assetWorkshopSelectedTexturePath = representative.Path;

            if (_assetWorkshopBuildMode)
                await LoadAssetWorkshopBuildPreviewAsync(group);
            else if (representative != null)
                await LoadAssetWorkshopTexturePreviewForEntry(representative);

            UpdateAssetWorkshopBuildModeState();
            return;
        }

        if (entry != null)
        {
            await SelectAssetWorkshopEntryAsync(entry);
            return;
        }

        AssetWorkshopExtractButton.IsEnabled = false;
        ClearAssetWorkshopPreview();
    }

    private async Task LoadAssetWorkshopTexturePreviewForEntry(AssetEntryItem entry)
    {
        StopAssetWorkshopAudio();
        HideAssetWorkshopAudioControls();

        AssetWorkshopPreviewName.Text = entry.AssetName;
        AssetWorkshopPreviewType.Text = entry.AssetType;
        AssetWorkshopPreviewSize.Text = entry.DisplaySize;
        AssetWorkshopPreviewPath.Text = entry.Path;
        if (AssetWorkshopPreviewSourceClass != null)
            AssetWorkshopPreviewSourceClass.Text = string.IsNullOrWhiteSpace(entry.SourceClass) ? entry.AssetType : entry.SourceClass;
        if (AssetWorkshopPreviewFiles != null)
            AssetWorkshopPreviewFiles.Text = entry.DisplayFiles;

        if (AssetWorkshopPreviewImage != null)
        {
            AssetWorkshopPreviewImage.Source = null;
            AssetWorkshopPreviewImage.Visibility = Visibility.Collapsed;
        }

        if (string.IsNullOrWhiteSpace(_assetWorkshopSelectedPak))
            return;

        AssetWorkshopPreviewStatus.Visibility = Visibility.Visible;
        AssetWorkshopPreviewStatus.Text = "Loading texture preview…";
        try
        {
            var previewPath = await Task.Run(() => DecodeTextureAssetPreview(_assetWorkshopSelectedPak!, entry.Path));
            if (!string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath) && AssetWorkshopPreviewImage != null)
            {
                var bytes = await File.ReadAllBytesAsync(previewPath);
                using var ms = new MemoryStream(bytes);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                AssetWorkshopPreviewImage.Source = image;
                AssetWorkshopPreviewImage.Visibility = Visibility.Visible;
                AssetWorkshopPreviewStatus.Visibility = Visibility.Collapsed;
            }
            else
            {
                AssetWorkshopPreviewStatus.Visibility = Visibility.Visible;
                AssetWorkshopPreviewStatus.Text = FindRepakExecutable() == null
                    ? "Texture preview requires repak.exe. Place it in Documents\\Retro Rewind Modhub\\Tools\\repak.exe."
                    : "The texture could not be decoded for preview.";
            }
        }
        catch (Exception ex)
        {
            AssetWorkshopPreviewStatus.Visibility = Visibility.Visible;
            AssetWorkshopPreviewStatus.Text = $"Preview failed: {ex.Message}";
        }
    }


    private async void AssetWorkshopExtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (_assetWorkshopBuildMode)
        {
            AssetWorkshopSelectImage();
            return;
        }

        AssetEntryItem? entry = null;

        // Asset rows are Buttons nested inside the ListBox, so the ListBox's
        // SelectedItem is not the authoritative selection. The row click
        // records the selected asset path.
        if (!string.IsNullOrWhiteSpace(_assetWorkshopSelectedTexturePath))
        {
            entry = _assetWorkshopEntries.FirstOrDefault(e =>
                string.Equals(
                    e.Path,
                    _assetWorkshopSelectedTexturePath,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (entry == null && AssetWorkshopEntriesList.SelectedItem is Grid selectedGrid)
        {
            entry = selectedGrid.Tag as AssetEntryItem
                    ?? (selectedGrid.Tag as AssetWorkshopTextureGroup)?.Entries.FirstOrDefault();
        }

        if (entry == null || string.IsNullOrWhiteSpace(_assetWorkshopSelectedPak))
            return;

        var isTexture = string.Equals(entry.AssetType, "Texture", StringComparison.OrdinalIgnoreCase);

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = isTexture
                ? "Choose where to extract the texture. The PNG will be placed directly in this folder."
                : $"Choose where to extract the {entry.AssetType} package. Package files will be placed directly in this folder.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        AssetWorkshopExtractButton.IsEnabled = false;
        AssetWorkshopStatus.Text = isTexture
            ? $"Extracting texture {entry.AssetName}…"
            : $"Extracting {entry.AssetType} package {entry.AssetName}…";

        try
        {
            var outputDirectory = dialog.SelectedPath;
            if (isTexture)
            {
                var outputPath = Path.Combine(outputDirectory, entry.AssetName + ".png");
                await Task.Run(() => ExtractTextureAsset(_assetWorkshopSelectedPak!, entry.Path, outputPath));
                AssetWorkshopStatus.Text = $"Extracted {entry.AssetName} directly to {outputDirectory}.";
            }
            else
            {
                var extracted = await Task.Run(() => ExtractAssetPackage(_assetWorkshopSelectedPak!, entry.Path, outputDirectory, true));
                AssetWorkshopStatus.Text = $"Exported {entry.AssetName} ({extracted} file(s)) directly to {outputDirectory}.";
            }
        }
        catch (Exception ex)
        {
            AssetWorkshopStatus.Text = $"{entry.AssetType} extraction failed: {ex.Message}";
        }
        finally
        {
            UpdateAssetWorkshopBuildModeState();
        }
    }

    private static int ExtractAssetPackage(string selectedPak, string assetPath, string outputDirectory, bool onlyUassetAndUexp = false)
    {
        Directory.CreateDirectory(outputDirectory);
        using var pakStream = File.OpenRead(selectedPak);
        var reader = CreateAssetPakReader(pakStream);
        var files = GetReaderFiles(reader);
        var companions = FindAssetCompanionFiles(assetPath, files);
        var requested = new[] { assetPath }
            .Concat(onlyUassetAndUexp ? companions.Where(p => p.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase)) : companions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count == 0)
            throw new InvalidOperationException("No package files were found for the selected asset.");

        var count = 0;
        foreach (var filePath in requested)
        {
            var leaf = Path.GetFileName(filePath.Replace('\\', '/'));
            if (string.IsNullOrWhiteSpace(leaf)) continue;
            var output = Path.Combine(outputDirectory, leaf);
            ExtractAssetEntry(reader, pakStream, filePath, output);
            count++;
        }

        return count;
    }

    private static void ExtractTextureAsset(string selectedPak, string assetPath, string outputPath)
    {
        var tempRoot = MaterializeTextureWithRepak(selectedPak, assetPath);
        try
        {
            var uasset = FindMaterializedUasset(tempRoot, assetPath);
            if (string.IsNullOrWhiteSpace(uasset) || !File.Exists(uasset))
                throw new InvalidOperationException("repak extracted the texture package, but the .uasset file could not be found.");

            var outputDir = Path.Combine(tempRoot, "export");
            var exported = RunAssetWorkshopInjector(uasset, outputDir, "png", out var error);
            if (string.IsNullOrWhiteSpace(exported) || !File.Exists(exported))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "The texture injector did not produce a PNG."
                    : error);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.Copy(exported, Path.ChangeExtension(outputPath, ".png"), true);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string MaterializeTextureWithRepak(string selectedPak, string assetPath)
    {
        var repak = FindRepakExecutable();
        if (string.IsNullOrWhiteSpace(repak))
            throw new InvalidOperationException("repak.exe was not found. Place it in Documents\\Retro Rewind Modhub\\Tools\\repak.exe.");

        if (!File.Exists(selectedPak))
            throw new FileNotFoundException("The vanilla RetroRewind-Windows.pak could not be found.", selectedPak);

        var tempRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "AssetWorkshop", Guid.NewGuid().ToString("N"));
        var unpacked = Path.Combine(tempRoot, "unpacked");
        Directory.CreateDirectory(unpacked);

        try
        {
            var normalized = assetPath.Replace('\\', '/').TrimStart('/');
            if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^7];

            EnsureAssetWorkshopOodleForRepak(selectedPak, repak);
            var error = RunRepakUnpackSelected(repak, selectedPak, normalized, unpacked);
            if (!string.IsNullOrWhiteSpace(error))
                throw new InvalidOperationException(error);

            var uasset = FindMaterializedUasset(tempRoot, normalized);
            if (string.IsNullOrWhiteSpace(uasset) || !File.Exists(uasset))
                throw new InvalidOperationException($"repak completed, but did not extract {Path.GetFileName(normalized)}.uasset.");

            return tempRoot;
        }
        catch
        {
            try { Directory.Delete(tempRoot, true); } catch { }
            throw;
        }
    }

    private static string? FindMaterializedUasset(string tempRoot, string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];

        var exact = Path.Combine(tempRoot, "unpacked", normalized.Replace('/', Path.DirectorySeparatorChar) + ".uasset");
        if (File.Exists(exact)) return exact;

        var leaf = Path.GetFileName(normalized) + ".uasset";
        try
        {
            return Directory.EnumerateFiles(Path.Combine(tempRoot, "unpacked"), leaf, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? RunAssetWorkshopInjector(
        string uassetPath,
        string outputDir,
        string format,
        out string error)
    {
        error = "";
        if (!format.Equals("png", StringComparison.OrdinalIgnoreCase))
        {
            error = "The independent Retro Rewind texture injector currently exports PNG only.";
            return null;
        }

        try
        {
            return ExternalTextureInjectorBridge.ExportPng(uassetPath, outputDir);
        }
        catch (Exception ex)
        {
            error = "Independent Retro Rewind texture injector failed: " + ex.Message;
            return null;
        }
    }

    private static string? DecodeTextureAssetPreview(string selectedPak, string assetPath)
    {
        var tempRoot = MaterializeTextureWithRepak(selectedPak, assetPath);
        try
        {
            var uasset = FindMaterializedUasset(tempRoot, assetPath);
            if (string.IsNullOrWhiteSpace(uasset)) return null;

            var previewDir = Path.Combine(tempRoot, "preview");
            var exported = RunAssetWorkshopInjector(uasset, previewDir, "png", out var error);
            if (string.IsNullOrWhiteSpace(exported) || !File.Exists(exported))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "The independent Retro Rewind texture injector did not produce a PNG."
                    : error);

            var preview = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "AssetWorkshop", "preview_" + Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
            File.Copy(exported, preview, true);
            return preview;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static string? RunRepakUnpackSelected(string repak, string pakPath, string assetPath, string outputDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = repak,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(repak) ?? AppContext.BaseDirectory
        };
        psi.ArgumentList.Add("unpack");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputDir);
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(assetPath + ".uasset");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(assetPath + ".uexp");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(assetPath + ".ubulk");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(assetPath + ".uptnl");
        psi.ArgumentList.Add(pakPath);

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return "Could not start repak.";
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return $"repak failed (exit code {process.ExitCode}).\n{stderr.Trim()}\n{stdout.Trim()}".Trim();
            return "";
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static void EnsureAssetWorkshopOodleForRepak(string selectedPak, string repak)
    {
        // repak's Oodle loader can obtain its own compatible native runtime.
        // Retro Rewind does not ship oo2core_9_win64.dll, so this feature does not
        // require or search for one in the game installation.
        try
        {
            var repakDirectory = Path.GetDirectoryName(repak);
            if (string.IsNullOrWhiteSpace(repakDirectory)) return;

            var localOodle = Path.Combine(repakDirectory, "oo2core_9_win64.dll");
            if (File.Exists(localOodle))
            {
                try { Environment.SetEnvironmentVariable("OODLE_PATH", localOodle); } catch { }
            }
        }
        catch { }
    }

    private static readonly object _assetWorkshopCueInitLock = new();
    private static bool _assetWorkshopCueCompressionInitialized;

    private static void EnsureAssetWorkshopCueCompressionInitialized()
    {
        if (_assetWorkshopCueCompressionInitialized) return;
        lock (_assetWorkshopCueInitLock)
        {
            if (_assetWorkshopCueCompressionInitialized) return;
            // CUE4Parse's Unreal texture decoder normally uses the native Detex
            // helper for BC7/ETC formats on Windows. ModHub should not require a
            // separately-installed native decoder, so prefer the bundled managed
            // AssetRipper decoder instead. This is especially important for UE5
            // desktop textures, which are commonly cooked as BC7.
            ZlibHelper.Initialize();
            OodleHelper.Initialize();
            TextureDecoder.UseAssetRipperTextureDecoder = true;
            _assetWorkshopCueCompressionInitialized = true;
        }
    }

    private static DefaultFileProvider CreateAssetWorkshopCueProvider(string pakDirectory)
    {
        EnsureAssetWorkshopCueCompressionInitialized();
        var version = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
        var provider = new DefaultFileProvider(pakDirectory, SearchOption.TopDirectoryOnly, true, version);
        provider.Initialize();
        provider.PostMount();
        return provider;
    }

    private static bool TryResolveAssetWorkshopGameFile(DefaultFileProvider provider, string assetPath, out GameFile? gameFile)
    {
        gameFile = null;
        if (provider == null || string.IsNullOrWhiteSpace(assetPath))
            return false;

        var requestedContent = GetContentRelativeAssetPath(assetPath);
        var requestedPackage = NormalizeAssetWorkshopPackageIdentity(requestedContent);

        // CUE4Parse may expose a cooked package as either:
        //   .../LA_Arcade_A_01.uasset
        // or:
        //   .../LA_Arcade_A_01
        // depending on the mounted archive/version. Compare both forms.
        foreach (var candidate in provider.Files.Values)
        {
            var candidatePath = candidate.Path.Replace('\\', '/').TrimStart('/');
            var candidateContent = GetContentRelativeAssetPath(candidatePath);
            var candidatePackage = NormalizeAssetWorkshopPackageIdentity(candidateContent);

            if (!candidate.IsUePackage && !candidateContent.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && !candidateContent.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(candidatePackage, requestedPackage, StringComparison.OrdinalIgnoreCase)
                || candidatePackage.EndsWith('/' + requestedPackage, StringComparison.OrdinalIgnoreCase)
                || requestedPackage.EndsWith('/' + candidatePackage, StringComparison.OrdinalIgnoreCase))
            {
                gameFile = candidate;
                return true;
            }
        }

        // Some CUE4Parse versions keep the archive-level GameFile objects separate
        // from provider.Files. Try the mounted archive as well, without assuming
        // that the archive has a particular key/name.
        try
        {
            foreach (var archiveName in new[] { "RetroRewind-Windows.pak", Path.GetFileName(assetPath) })
            {
                if (string.IsNullOrWhiteSpace(archiveName)) continue;
                try
                {
                    var archive = provider.GetArchive(archiveName);
                    foreach (var candidate in archive.Files.Values)
                    {
                        var candidatePath = candidate.Path.Replace('\\', '/').TrimStart('/');
                        var candidateContent = GetContentRelativeAssetPath(candidatePath);
                        var candidatePackage = NormalizeAssetWorkshopPackageIdentity(candidateContent);
                        if (!candidate.IsUePackage
                            && !candidateContent.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                            && !candidateContent.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(candidatePackage, requestedPackage, StringComparison.OrdinalIgnoreCase)
                            || candidatePackage.EndsWith('/' + requestedPackage, StringComparison.OrdinalIgnoreCase)
                            || requestedPackage.EndsWith('/' + candidatePackage, StringComparison.OrdinalIgnoreCase))
                        {
                            gameFile = candidate;
                            return true;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        // Use CUE4Parse's native lookup as well. Its public API expects the
        // physical game package form (<Project>/Content/...) for mounted PAKs,
        // while /Game/... is the virtual Unreal object form. Try both forms and
        // the exact requested path before giving up.
        var directCandidates = new[]
        {
            assetPath.Replace('\\', '/').TrimStart('/'),
            requestedContent.TrimStart('/'),
            "RetroRewind/Content/" + requestedPackage.TrimStart('/'),
            NormalizeAssetWorkshopCuePath(assetPath),
            "/Game/" + requestedPackage.TrimStart('/')
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var packagePath in directCandidates)
        {
            try
            {
                if (provider.TryGetGameFile(packagePath, out var resolved) && resolved != null)
                {
                    gameFile = resolved;
                    return true;
                }
            }
            catch { }
        }

        // If the archive mounted but CUE4Parse's path normalizer still could not
        // resolve the package, surface enough state to diagnose the mount instead
        // of falsely reporting a package-format problem.
        var fileCount = provider.Files?.Count ?? 0;
        var projectName = provider.ProjectName ?? "";
        var sample = provider.Files?.Values
            .Where(f => f != null && (f.Path.Contains("LA_AdultDoor_A_01", StringComparison.OrdinalIgnoreCase)
                                   || f.Path.Contains("VideoStore/asset/meshes", StringComparison.OrdinalIgnoreCase)))
            .Take(3)
            .Select(f => f.Path)
            .ToList() ?? new List<string>();

        throw new InvalidOperationException(
            $"CUE4Parse mounted {fileCount:N0} file(s) (ProjectName='{projectName}'), but could not resolve '{requestedContent}'." +
            (sample.Count > 0 ? $" Matching archive entries: {string.Join(" | ", sample)}" : " No matching archive entries were exposed by CUE4Parse."));
    }

    private static string NormalizeAssetWorkshopPackageIdentity(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        while (normalized.StartsWith("../", StringComparison.Ordinal))
            normalized = normalized[3..];
        normalized = normalized.TrimStart('/');

        if (normalized.StartsWith("Game/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Game/".Length..];
        if (normalized.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["Content/".Length..];

        var contentMarker = "/Content/";
        var markerIndex = normalized.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            normalized = normalized[(markerIndex + contentMarker.Length)..];

        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];
        else if (normalized.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^5];

        return normalized.Trim('/');
    }

    private static string GetContentRelativeAssetPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var marker = "/Content/";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
            return normalized[(index + marker.Length)..].TrimStart('/');

        if (normalized.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            return normalized["Content/".Length..].TrimStart('/');

        return normalized;
    }

    private static bool TryLoadAssetWorkshopPackage(DefaultFileProvider provider, string assetPath, out IPackage? package)
    {
        package = null;
        if (provider == null || string.IsNullOrWhiteSpace(assetPath))
            return false;

        try
        {
            // Resolve the actual GameFile first. This is important for cooked PAKs:
            // LoadPackage(GameFile) asks CUE4Parse to locate the matching .uexp,
            // .ubulk and .uptnl payloads through the provider's file table.
            if (!TryResolveAssetWorkshopGameFile(provider, assetPath, out var gameFile) || gameFile == null)
                return false;

            return provider.TryLoadPackage(gameFile, out package) && package != null;
        }
        catch
        {
            package = null;
            return false;
        }
    }

    private static T? TryLoadAssetWorkshopObject<T>(DefaultFileProvider provider, string assetPath) where T : UObject
    {
        if (!TryResolveAssetWorkshopGameFile(provider, assetPath, out var gameFile) || gameFile == null)
            return null;

        try
        {
            var objectName = Path.GetFileNameWithoutExtension(gameFile.Path);
            return provider.LoadPackageObject<T>(gameFile.Path, objectName);
        }
        catch
        {
            return null;
        }
    }

    private static UnrealAssetIdentity? IdentifySelectedAsset(string selectedPak, string assetPath)
    {
        try
        {
            var pakDirectory = Path.GetDirectoryName(selectedPak);
            if (string.IsNullOrWhiteSpace(pakDirectory) || !Directory.Exists(pakDirectory)) return null;
            using var provider = CreateAssetWorkshopCueProvider(pakDirectory);
            if (!TryLoadAssetWorkshopPackage(provider, assetPath, out var package) || package == null)
            {
                if (!TryLoadAssetWorkshopPackageFromLooseFiles(selectedPak, assetPath, out package, out var tempRoot) || package == null)
                    return null;
                try { return IdentifyAssetFromCuePackage(package, assetPath); }
                finally { try { if (!string.IsNullOrWhiteSpace(tempRoot)) Directory.Delete(tempRoot, true); } catch { } }
            }
            return IdentifyAssetFromCuePackage(package, assetPath);
        }
        catch
        {
            return null;
        }
    }

    private void EnsureAssetWorkshopAudioTimer()
    {
        if (_assetWorkshopAudioTimer.IsEnabled)
            return;

        _assetWorkshopAudioTimer.Tick += AssetWorkshopAudioTimer_Tick;
    }

    private void AssetWorkshopAudioTimer_Tick(object? sender, EventArgs e)
    {
        if (_assetWorkshopAudioPlayer == null || !_assetWorkshopAudioPlayer.IsPlaying)
            return;

        if (_assetWorkshopAudioPlayer.Length > 0)
        {
            AssetWorkshopAudioProgress.Maximum = _assetWorkshopAudioPlayer.Length;
            AssetWorkshopAudioProgress.Value = Math.Clamp((double)_assetWorkshopAudioPlayer.Time, 0, Math.Max(1, (double)_assetWorkshopAudioPlayer.Length));
            AssetWorkshopAudioTime.Text = $"{FormatAudioTime(_assetWorkshopAudioPlayer.Time)} / {FormatAudioTime(_assetWorkshopAudioPlayer.Length)}";
        }
    }

    private static string FormatAudioTime(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var ts = TimeSpan.FromMilliseconds(milliseconds);
        return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    private async Task LoadAssetWorkshopAudioPreviewAsync(AssetEntryItem entry)
    {
        StopAssetWorkshopAudio();
        ShowAssetWorkshopAudioControls();
        AssetWorkshopPreviewImage.Visibility = Visibility.Collapsed;
        AssetWorkshopPreviewStatus.Visibility = Visibility.Visible;
        AssetWorkshopPreviewStatus.Text = "Decoding audio preview…";

        try
        {
            if (string.IsNullOrWhiteSpace(_assetWorkshopSelectedPak))
                throw new InvalidOperationException("No vanilla PAK is selected.");

            var result = await Task.Run(() => DecodeAssetWorkshopAudio(_assetWorkshopSelectedPak!, entry.Path));
            if (result == null || result.Value.Bytes.Length == 0)
                throw new InvalidOperationException("CUE4Parse could not decode this SoundWave.");

            var extension = SanitizeAudioExtension(result.Value.Format);
            if (extension.Equals("wem", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Wwise audio was resolved to WEM, but vgmstream-cli was not found in Documents\\Retro Rewind Modhub\\Tools.");
            var tempRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "AssetWorkshop", "Audio");
            Directory.CreateDirectory(tempRoot);
            var file = Path.Combine(tempRoot, Guid.NewGuid().ToString("N") + "." + extension);
            await File.WriteAllBytesAsync(file, result.Value.Bytes);
            _assetWorkshopAudioFile = file;

            var libDir = RequireLibVlcForVideoEditor();
            if (_assetWorkshopAudioLibVlc == null)
            {
                if (_videoEditorLibVlc != null)
                    _assetWorkshopAudioLibVlc = _videoEditorLibVlc;
                else
                {
                    Core.Initialize(libDir);
                    _assetWorkshopAudioLibVlc = new LibVLC(false, true, "--no-video-title-show", "--no-osd");
                }
            }

            _assetWorkshopAudioPlayer = new LibVLCSharp.Shared.MediaPlayer(_assetWorkshopAudioLibVlc)
            {
                EnableHardwareDecoding = false,
                Mute = false
            };
            _assetWorkshopAudioMedia = new Media(_assetWorkshopAudioLibVlc, file, FromType.FromPath);
            _assetWorkshopAudioPlayer.EndReached += AssetWorkshopAudioPlayer_EndReached;
            _assetWorkshopAudioPlayer.EncounteredError += AssetWorkshopAudioPlayer_EncounteredError;
            _assetWorkshopAudioPlayer.Play(_assetWorkshopAudioMedia);

            AssetWorkshopAudioVolume.Value = 100;
            AssetWorkshopAudioProgress.Value = 0;
            AssetWorkshopAudioTime.Text = "0:00";
            AssetWorkshopPreviewStatus.Text = $"Playing {result.Value.Format.ToUpperInvariant()} audio.";
            _assetWorkshopAudioTimer.Start();
        }
        catch (Exception ex)
        {
            StopAssetWorkshopAudio();
            AssetWorkshopPreviewStatus.Text = $"Audio preview failed: {ex.Message}";
        }
    }

    private static string SanitizeAudioExtension(string format)
    {
        var value = (format ?? "").Trim().TrimStart('.');
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8 || value.Any(ch => !char.IsLetterOrDigit(ch)))
            return "audio";
        return value.ToLowerInvariant();
    }

    private static IEnumerable<string> BuildAssetWorkshopObjectPaths(string assetPath)
    {
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^7];
        else if (normalized.EndsWith(".umap", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^5];

        var objectName = Path.GetFileName(normalized);
        var contentRelative = GetContentRelativeAssetPath(normalized);
        var candidates = new[]
        {
            normalized + "." + objectName,
            "/" + normalized + "." + objectName,
            "/Game/" + contentRelative + "." + objectName,
            "Game/" + contentRelative + "." + objectName,
            "/Game/" + contentRelative + "." + objectName
        };
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private (string Format, byte[] Bytes)? DecodeAssetWorkshopAudio(string selectedPak, string assetPath)
    {
        string? looseTempRoot = null;
        try
        {
            var pakDirectory = Path.GetDirectoryName(selectedPak);
            if (string.IsNullOrWhiteSpace(pakDirectory) || !Directory.Exists(pakDirectory))
                return null;

            EnsureAssetWorkshopCueCompressionInitialized();
            using var provider = CreateAssetWorkshopCueProvider(pakDirectory);

            UObject? soundObject = null;
            IPackage? package = null;

            if (TryLoadAssetWorkshopPackage(provider, assetPath, out package) && package != null)
            {
                soundObject = package.GetExports().FirstOrDefault(x =>
                    x is USoundWave || x is UAkMediaAssetData || x is UAkAudioEvent);
            }

            if (soundObject == null)
            {
                if (TryLoadAssetWorkshopPackageFromLooseFiles(
                        selectedPak, assetPath, out var loosePackage, out looseTempRoot)
                    && loosePackage != null)
                {
                    package = loosePackage;
                    soundObject = package.GetExports().FirstOrDefault(x =>
                        x is USoundWave || x is UAkMediaAssetData || x is UAkAudioEvent);
                }
            }

            if (soundObject == null)
            {
                foreach (var candidate in BuildAssetWorkshopObjectPaths(assetPath))
                {
                    try
                    {
                        var soundWave = TryLoadAssetWorkshopObject<USoundWave>(provider, candidate);
                        if (soundWave != null) { soundObject = soundWave; break; }

                        var akMedia = TryLoadAssetWorkshopObject<UAkMediaAssetData>(provider, candidate);
                        if (akMedia != null) { soundObject = akMedia; break; }

                        var akEvent = TryLoadAssetWorkshopObject<UAkAudioEvent>(provider, candidate);
                        if (akEvent != null) { soundObject = akEvent; break; }
                    }
                    catch { }
                }
            }

            if (soundObject == null)
            {
                var packagePath = NormalizeAssetWorkshopPackageIdentity(assetPath);
                var packageState = package != null
                    ? "The package loaded, but it contained no supported USoundWave/UAkMediaAssetData/UAkAudioEvent export."
                    : "The package itself could not be loaded from the mounted PAK or materialized loose files.";
                throw new InvalidOperationException(
                    $"CUE4Parse could not load the selected audio package/export: {packagePath}. {packageState}");
            }

            // Retro Rewind's cooked SoundWave assets can use Unreal's Bink Audio
            // compression. In that case the actual compressed stream lives in the
            // companion .ubulk while the .uexp contains the small Bink header.
            // Do not send that raw BINKA payload through SoundDecoder: build the
            // exact input expected by binkadec and let the dedicated Bink decoder
            // produce a normal WAV for LibVLC.
            if (soundObject is USoundWave)
            {
                // A direct CUE4Parse load may succeed while still leaving the
                // companion .ubulk outside the object. Materialize the selected
                // package as well so the raw Bink payload is always available.
                if (string.IsNullOrWhiteSpace(looseTempRoot))
                {
                    TryLoadAssetWorkshopPackageFromLooseFiles(
                        selectedPak, assetPath, out _, out looseTempRoot);
                }

                var bink = TryDecodeBinkAudioFromMaterializedPackage(looseTempRoot, assetPath);
                if (bink.HasValue && bink.Value.Bytes.Length > 0)
                    return bink.Value;
            }

            // Wwise Audio Events are wrappers around one or more Wwise media objects.
            // They are not USoundWave assets and must be resolved through CUE4Parse's
            // WwiseProvider (event -> SoundBank/media -> WEM) before playback.
            if (soundObject is UAkAudioEvent audioEvent)
            {
                var wwise = new WwiseProvider(provider, pakDirectory);
                var sounds = wwise.ExtractAudioEventSounds(audioEvent);
                var sound = sounds.FirstOrDefault(x => x.Data?.IsValid == true);
                if (sound == null)
                    throw new InvalidOperationException(
                        $"Wwise event '{audioEvent.Name}' was loaded, but CUE4Parse could not resolve any WEM media.");

                var bytes = sound.GetData();
                if (bytes.Length == 0)
                    throw new InvalidOperationException(
                        $"Wwise event '{audioEvent.Name}' resolved to empty media data.");

                // The Wwise provider returns WEM. Playback conversion is handled by
                // vgmstream when it is installed in the ModHub Tools directory.
                var wemResult = TryConvertWemWithVgmstream(bytes, sound.OutputPath);
                if (wemResult.HasValue)
                    return wemResult.Value;

                // Keep the raw WEM as a last-resort playback candidate. Some VLC builds
                // can handle particular Wwise codecs, while others cannot.
                return ("wem", bytes);
            }

            try
            {
                SoundDecoder.Decode(soundObject, true, out string audioFormat, out byte[] soundData);
                if (soundData == null || soundData.Length == 0)
                    throw new InvalidOperationException("CUE4Parse returned no decoded audio data.");

                return (audioFormat, soundData);
            }
            catch (Exception decodeEx)
            {
                var runtimeFormat = GetAssetWorkshopSoundWaveRuntimeFormat(soundObject);
                var detail = string.IsNullOrWhiteSpace(runtimeFormat)
                    ? ""
                    : $" Runtime format: {runtimeFormat}.";
                throw new InvalidOperationException(
                    $"CUE4Parse SoundDecoder failed: {decodeEx.Message}{detail}", decodeEx);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Audio package processing failed: {ex.Message}", ex);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(looseTempRoot))
            {
                if (_assetWorkshopLooseProviders.Remove(looseTempRoot, out var looseProvider))
                {
                    try { looseProvider.Dispose(); } catch { }
                }
                try { Directory.Delete(looseTempRoot, true); } catch { }
            }
        }
    }

    private (string Format, byte[] Bytes)? TryDecodeBinkAudioFromMaterializedPackage(string? tempRoot, string assetPath)
    {
        if (string.IsNullOrWhiteSpace(tempRoot) || !Directory.Exists(tempRoot))
            return null;

        try
        {
            var normalized = assetPath.Replace('\\', '/').TrimStart('/');
            if (normalized.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[..^7];

            var baseName = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(baseName))
                return null;

            string? uexp = null;
            string? ubulk = null;
            foreach (var file in Directory.EnumerateFiles(tempRoot, baseName + ".uexp", SearchOption.AllDirectories))
            {
                uexp = file;
                break;
            }
            foreach (var file in Directory.EnumerateFiles(tempRoot, baseName + ".ubulk", SearchOption.AllDirectories))
            {
                ubulk = file;
                break;
            }

            if (string.IsNullOrWhiteSpace(uexp) || string.IsNullOrWhiteSpace(ubulk)
                || !File.Exists(uexp) || !File.Exists(ubulk))
                return null;

            // UE5 cooked Bink Audio packages contain an ABEU marker in the .uexp.
            // binkadec expects that marker plus the following 24 bytes, followed
            // immediately by the .ubulk stream. This is the same raw-package
            // layout used by UE2WAV/Binkadec-based UE5 extraction tools.
            var uexpBytes = File.ReadAllBytes(uexp);
            var marker = new byte[] { (byte)'A', (byte)'B', (byte)'E', (byte)'U' };
            var markerIndex = -1;
            for (var i = 0; i <= uexpBytes.Length - marker.Length - 24; i++)
            {
                var match = true;
                for (var j = 0; j < marker.Length; j++)
                {
                    if (uexpBytes[i + j] != marker[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    markerIndex = i;
                    break;
                }
            }

            if (markerIndex < 0)
                return null;

            var headerLength = marker.Length + 24;
            var inputPath = Path.Combine(tempRoot, "binka_input.tmp");
            var outputPath = Path.Combine(tempRoot, "binka_output.wav");
            try
            {
                using (var output = new FileStream(inputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    output.Write(uexpBytes, markerIndex, headerLength);
                    using var bulk = new FileStream(ubulk, FileMode.Open, FileAccess.Read, FileShare.Read);
                    bulk.CopyTo(output);
                }

                var binkExe = FindBinkAudioDecoder();
                if (string.IsNullOrWhiteSpace(binkExe))
                    throw new InvalidOperationException(
                        "This SoundWave uses BINKA audio. Install binkadec.exe in Documents\\Retro Rewind Modhub\\Tools\\BinkAudio\\ (or Tools\\binkadec.exe) to enable audio preview.");

                var psi = new ProcessStartInfo
                {
                    FileName = binkExe,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(binkExe) ?? AppContext.BaseDirectory
                };
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add(outputPath);

                using var process = Process.Start(psi);
                if (process == null)
                    throw new InvalidOperationException("Could not start binkadec.exe.");

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                var stdout = stdoutTask.GetAwaiter().GetResult();
                var stderr = stderrTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    var detail = string.Join(" ", new[] { stderr.Trim(), stdout.Trim() }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(detail)
                            ? $"binkadec.exe failed with exit code {process.ExitCode}."
                            : $"binkadec.exe failed: {detail}");
                }

                var wav = File.ReadAllBytes(outputPath);
                return wav.Length == 0 ? null : ("wav", wav);
            }
            finally
            {
                try { if (File.Exists(inputPath)) File.Delete(inputPath); } catch { }
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private string AssetWorkshopToolsDirectory => Path.Combine(GetVerifiedGameRoot(), "Tools");

    private string? FindBinkAudioDecoder()
    {
        var candidates = new[]
        {
            Path.Combine(AssetWorkshopToolsDirectory, "BinkAudio", "binkadec.exe"),
            Path.Combine(AssetWorkshopToolsDirectory, "binkadec.exe"),
            Path.Combine(AssetWorkshopToolsDirectory, "binkadec", "binkadec.exe")
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        return null;
    }

    private static (string Format, byte[] Bytes)? TryConvertWemWithVgmstream(byte[] wemBytes, string? sourceName)
    {
        try
        {
            var toolsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Retro Rewind Modhub", "Tools");

            var candidates = new[]
            {
                Path.Combine(toolsRoot, "vgmstream", "vgmstream-cli.exe"),
                Path.Combine(toolsRoot, "vgmstream-cli.exe"),
                Path.Combine(toolsRoot, "vgmstream", "win-x64", "vgmstream-cli.exe")
            };
            var exe = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(exe))
                return null;

            var tempRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "AssetWorkshop", "Wwise");
            Directory.CreateDirectory(tempRoot);
            var baseName = Path.GetFileNameWithoutExtension(sourceName ?? "audio");
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "audio";
            var id = Guid.NewGuid().ToString("N");
            var wemPath = Path.Combine(tempRoot, id + ".wem");
            var wavPath = Path.Combine(tempRoot, id + ".wav");
            File.WriteAllBytes(wemPath, wemBytes);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-o \"{wavPath}\" \"{wemPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(psi);
            if (process == null) return null;
            process.WaitForExit(15000);
            if (!process.HasExited || !File.Exists(wavPath)) return null;

            var wav = File.ReadAllBytes(wavPath);
            try { File.Delete(wemPath); } catch { }
            try { File.Delete(wavPath); } catch { }
            return ("wav", wav);
        }
        catch
        {
            return null;
        }
    }

    private static string GetAssetWorkshopSoundWaveRuntimeFormat(UObject sound)
    {
        try
        {
            var type = sound.GetType();
            foreach (var name in new[] { "RuntimeFormat", "Format", "CompressionFormat", "AudioFormat" })
            {
                var prop = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                {
                    var value = prop.GetValue(sound);
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        return value.ToString()!;
                }

                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    var value = field.GetValue(sound);
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        return value.ToString()!;
                }
            }
        }
        catch { }

        return string.Empty;
    }

    private void ShowAssetWorkshopAudioControls()
    {
        AssetWorkshopAudioControls.Visibility = Visibility.Visible;
    }

    private void HideAssetWorkshopAudioControls()
    {
        if (AssetWorkshopAudioControls != null)
            AssetWorkshopAudioControls.Visibility = Visibility.Collapsed;
        if (AssetWorkshopAudioTime != null)
            AssetWorkshopAudioTime.Text = "0:00";
    }

    private void StopAssetWorkshopAudio()
    {
        try { _assetWorkshopAudioTimer.Stop(); } catch { }
        try { if (_assetWorkshopAudioPlayer != null) _assetWorkshopAudioPlayer.Stop(); } catch { }
        try { if (_assetWorkshopAudioMedia != null) _assetWorkshopAudioMedia.Dispose(); } catch { }
        try { if (_assetWorkshopAudioPlayer != null) _assetWorkshopAudioPlayer.Dispose(); } catch { }
        _assetWorkshopAudioMedia = null;
        _assetWorkshopAudioPlayer = null;
        if (!string.IsNullOrWhiteSpace(_assetWorkshopAudioFile))
        {
            try { File.Delete(_assetWorkshopAudioFile); } catch { }
        }
        _assetWorkshopAudioFile = null;
    }

    private void AssetWorkshopAudioPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_assetWorkshopAudioPlayer == null) return;
        if (_assetWorkshopAudioPlayer.IsPlaying) _assetWorkshopAudioPlayer.Pause();
        else _assetWorkshopAudioPlayer.Play();
    }

    private void AssetWorkshopAudioStop_Click(object sender, RoutedEventArgs e)
    {
        if (_assetWorkshopAudioPlayer == null) return;
        _assetWorkshopAudioPlayer.Stop();
        AssetWorkshopAudioProgress.Value = 0;
        AssetWorkshopAudioTime.Text = "0:00";
    }

    private void AssetWorkshopAudioProgress_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_assetWorkshopAudioPlayer == null) return;
        _assetWorkshopAudioPlayer.Time = (long)AssetWorkshopAudioProgress.Value;
    }

    private void AssetWorkshopAudioVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_assetWorkshopAudioPlayer != null)
            _assetWorkshopAudioPlayer.Volume = (int)Math.Clamp(e.NewValue, 0, 100);
    }

    private void AssetWorkshopAudioPlayer_EndReached(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AssetWorkshopAudioProgress.Value = 0;
            AssetWorkshopAudioTime.Text = "0:00";
        }));
    }

    private void AssetWorkshopAudioPlayer_EncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AssetWorkshopPreviewStatus.Text = "LibVLC could not play the decoded audio.";
        }));
    }

    private static byte[]? DecodeTextureFromRepakMaterializedPackage(string selectedPak, string assetPath)
    {
        var repak = FindRepakExecutable();
        if (string.IsNullOrWhiteSpace(repak)) return null;

        var tempRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "AssetWorkshop", Guid.NewGuid().ToString("N"));
        try
        {
            if (!TryExtractAssetPackageWithRepak(selectedPak, assetPath, tempRoot)) return null;

            EnsureAssetWorkshopCueCompressionInitialized();
            var version = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
            using var provider = new DefaultFileProvider(tempRoot, SearchOption.AllDirectories, true, version);
            provider.Initialize();
            provider.PostMount();

            var primary = FindMatchingArchivePath(assetPath, GetFilesFromDirectory(tempRoot), ".uasset", ".umap") ?? assetPath;
            if (!TryResolveAssetWorkshopGameFile(provider, primary, out var gameFile) || gameFile == null)
                return null;
            if (!provider.TryLoadPackage(gameFile, out var package) || package == null)
                return null;

            return DecodeFirstTextureExport(package);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static List<string> GetFilesFromDirectory(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private static byte[]? DecodeFirstTextureExport(IPackage package)
    {
        // Do not require UTexture2D specifically. UE5 cooked content can contain
        // UTexture2DArray/UTextureCube-derived texture assets as well. Decode()
        // on the UTexture base handles normal 2D textures and the supported
        // virtual-texture path; array/cube-specific exports can still expose a
        // representative first image through the base decoder.
        var texture = package.GetExports().OfType<UTexture>().FirstOrDefault();
        if (texture == null) return null;

        var bitmap = texture.Decode(ETexturePlatform.DesktopMobile);
        if (bitmap == null) return null;

        return bitmap.Encode(ETextureFormat.Png, false, out _);
    }

    private static bool TryLoadAssetWorkshopPackageFromLooseFiles(string selectedPak, string assetPath, out IPackage? package, out string tempRoot)
    {
        package = null;
        tempRoot = "";
        if (!File.Exists(selectedPak)) return false;

        try
        {
            // IMPORTANT: use repak for the selected cooked package when available.
            // repak is the proven UE PAK reader/unpacker path we are using for the
            // Asset Workshop asset pipeline. It extracts the real .uasset/.uexp and
            // bulk payloads without asking CUE4Parse to resolve them from inside the
            // mounted archive.
            tempRoot = Path.Combine(Path.GetTempPath(), "RetroRewindModHub", "AssetWorkshop", Guid.NewGuid().ToString("N"));
            var contentRoot = Path.Combine(tempRoot, "Content");
            Directory.CreateDirectory(contentRoot);

            if (TryExtractAssetPackageWithRepak(selectedPak, assetPath, tempRoot))
            {
                EnsureAssetWorkshopCueCompressionInitialized();
                var version = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
                var looseProvider = new DefaultFileProvider(tempRoot, SearchOption.AllDirectories, true, version);
                looseProvider.Initialize();
                looseProvider.PostMount();

                // The repak/materialized path is the most reliable route for cooked
                // UE5 packages. Keep the provider alive because CUE4Parse packages
                // lazily read their .uexp/.ubulk payloads during export decoding.
                var materializedFiles = GetFilesFromDirectory(tempRoot);
                var materializedPrimary =
                    FindMatchingArchivePath(assetPath, materializedFiles, ".uasset", ".umap");

                if (!string.IsNullOrWhiteSpace(materializedPrimary)
                    && TryResolveAssetWorkshopGameFile(looseProvider, materializedPrimary, out var looseGameFile)
                    && looseGameFile != null
                    && looseProvider.TryLoadPackage(looseGameFile, out package)
                    && package != null)
                {
                    _assetWorkshopLooseProviders[tempRoot] = looseProvider;
                    return true;
                }

                try { looseProvider.Dispose(); } catch { }
            }

            // Keep the existing built-in reader as a fallback for installations where
            // repak is not present. This preserves compatibility with the previous
            // Asset Workshop implementation while making repak the preferred path.
            using var pakStream = File.OpenRead(selectedPak);
            var reader = CreateAssetPakReader(pakStream);
            var allFiles = GetReaderFiles(reader);
            var primary = FindMatchingArchivePath(assetPath, allFiles, ".uasset", ".umap");
            if (string.IsNullOrWhiteSpace(primary)) return false;

            var companions = FindAssetCompanionFiles(primary, allFiles);
            var assetFiles = new[] { primary }.Concat(companions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var filePath in assetFiles)
            {
                // Preserve the Unreal project root (RetroRewind/Content/...) when
                // materializing a package. CUE4Parse uses that first path segment
                // to determine ProjectName for loose files; stripping it to just
                // Content/... makes FixPath resolve /Game/... to the wrong project.
                var relative = GetLoosePackageRelativePath(filePath);
                var destination = Path.Combine(tempRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                ExtractAssetEntry(reader, pakStream, filePath, destination);
            }

            EnsureAssetWorkshopCueCompressionInitialized();
            var fallbackVersion = new VersionContainer(EGame.GAME_UE5_4, ETexturePlatform.DesktopMobile);
            var fallbackProvider = new DefaultFileProvider(tempRoot, SearchOption.AllDirectories, true, fallbackVersion);
            fallbackProvider.Initialize();
            fallbackProvider.PostMount();

            if (TryResolveAssetWorkshopGameFile(fallbackProvider, primary, out var fallbackGameFile)
                && fallbackGameFile != null
                && fallbackProvider.TryLoadPackage(fallbackGameFile, out package)
                && package != null)
            {
                // The fallback provider must remain alive while package exports are
                // decoded. Store it in the temporary provider cache for the caller.
                _assetWorkshopLooseProviders[tempRoot] = fallbackProvider;
                return true;
            }

            try { fallbackProvider.Dispose(); } catch { }
            try { Directory.Delete(tempRoot, true); } catch { }
            tempRoot = "";
            return false;
        }
        catch
        {
            try { if (!string.IsNullOrWhiteSpace(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            tempRoot = "";
            return false;
        }
    }

    private static readonly Dictionary<string, DefaultFileProvider> _assetWorkshopLooseProviders = new(StringComparer.OrdinalIgnoreCase);

    private static string? FindRepakExecutable()
    {
        // repak is an external tool, so it belongs in the user's ModHub
        // Documents\Tools directory rather than beside the application or in the game.
        var toolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Retro Rewind Modhub",
            "Tools");

        var candidates = new[]
        {
            Path.Combine(toolsDir, "repak.exe"),
            Path.Combine(toolsDir, "repak", "repak.exe")
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "repak.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }

        return null;
    }

    private static bool TryExtractAssetPackageWithRepak(
        string selectedPak, string assetPath, string outputRoot)
    {
        var repak = FindRepakExecutable();
        if (string.IsNullOrWhiteSpace(repak) || !File.Exists(selectedPak))
            return false;

        try
        {
            var archiveFiles = RunRepakList(repak, selectedPak);
            if (archiveFiles.Count == 0)
                return false;

            var primary = FindMatchingArchivePath(
                assetPath, archiveFiles, ".uasset", ".umap");

            if (string.IsNullOrWhiteSpace(primary))
                return false;

            var assetFiles = new[] { primary }
                .Concat(FindAssetCompanionFiles(primary, archiveFiles))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var filePath in assetFiles)
            {
                var relative = GetLoosePackageRelativePath(filePath);
                if (string.IsNullOrWhiteSpace(relative))
                    return false;

                var destination = Path.Combine(
                    outputRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(
                    Path.GetDirectoryName(destination)!);

                if (!RunRepakGet(repak, selectedPak, filePath, destination))
                    return false;

                if (!File.Exists(destination) ||
                    new FileInfo(destination).Length == 0)
                    return false;
            }

            var primaryRelative = GetLoosePackageRelativePath(primary);
            var primaryOutput = Path.Combine(
                outputRoot,
                primaryRelative.Replace('/', Path.DirectorySeparatorChar));

            return File.Exists(primaryOutput) &&
                   new FileInfo(primaryOutput).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static List<string> RunRepakList(string repakExe, string pakPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = repakExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory =
                Path.GetDirectoryName(repakExe) ?? AppContext.BaseDirectory
        };

        psi.ArgumentList.Add("list");
        psi.ArgumentList.Add(pakPath);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start repak.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"repak list failed (exit code {process.ExitCode}). " +
                $"{stderr.Trim()}".Trim());
        }

        return stdout
            .Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line =>
                line.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".uexp", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".ubulk", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".uptnl", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool RunRepakGet(string repakExe, string pakPath, string archivePath, string outputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = repakExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(repakExe) ?? AppContext.BaseDirectory
        };
        psi.ArgumentList.Add("get");
        psi.ArgumentList.Add(pakPath);
        psi.ArgumentList.Add(archivePath.Replace('\\', '/').TrimStart('/'));

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return false;
            using var output = File.Create(outputPath);
            process.StandardOutput.BaseStream.CopyTo(output);
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                try { File.Delete(outputPath); } catch { }
                return false;
            }
            return new FileInfo(outputPath).Length > 0;
        }
        catch
        {
            try { File.Delete(outputPath); } catch { }
            return false;
        }
    }

    private static bool TryExtractSelectedAssetForWorkshop(string selectedPak, string assetPath, string tempRoot)
    {
        Directory.CreateDirectory(tempRoot);
        return TryExtractAssetPackageWithRepak(selectedPak, assetPath, tempRoot);
    }

    private static string GetLoosePackageRelativePath(string archivePath)
    {
        // PAK entries commonly look like ../../../RetroRewind/Content/... .
        // For a loose CUE4Parse provider we must preserve RetroRewind/Content,
        // while removing only the virtual mount traversal prefix.
        var normalized = archivePath.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("../", StringComparison.Ordinal))
            normalized = normalized[3..];
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (normalized.StartsWith("Game/", StringComparison.OrdinalIgnoreCase))
            normalized = "RetroRewind/Content/" + normalized[5..];
        else if (normalized.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            normalized = "RetroRewind/" + normalized;

        // Never allow an archive entry to escape the temporary workspace.
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p is "." or ".."))
            throw new InvalidOperationException("The package path contains an unsafe traversal component.");
        return string.Join('/', parts);
    }

    private static string? FindMatchingArchivePath(string requestedPath, List<string> allFiles, params string[] allowedExtensions)
    {
        var requested = GetContentRelativeAssetPath(requestedPath);
        var requestedName = Path.GetFileNameWithoutExtension(requested);
        var requestedDirectory = Path.GetDirectoryName(requested)?.Replace('\\', '/') ?? "";

        foreach (var file in allFiles)
        {
            var candidate = file.Replace('\\', '/').TrimStart('/');
            if (allowedExtensions.Length > 0 && !allowedExtensions.Any(ext => candidate.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                continue;

            var candidateContent = GetContentRelativeAssetPath(candidate);
            if (string.Equals(candidateContent, requested, StringComparison.OrdinalIgnoreCase))
                return file;

            var candidateName = Path.GetFileNameWithoutExtension(candidateContent);
            var candidateDirectory = Path.GetDirectoryName(candidateContent)?.Replace('\\', '/') ?? "";
            if (string.Equals(candidateName, requestedName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateDirectory, requestedDirectory, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private static string SafeAssetRelativePath(string archivePath)
    {
        var parts = archivePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safe = new List<string>();
        foreach (var part in parts)
        {
            if (part is "." or "..") continue;
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            safe.Add(part);
        }
        if (safe.Count == 0) safe.Add("extracted_asset");
        return Path.Combine(safe.ToArray());
    }

    private static void ExtractLogicalAsset(string pakPath, string assetPath, string destination)
    {
        using var pakStream = File.OpenRead(pakPath);
        var reader = CreateAssetPakReader(pakStream);
        var files = GetReaderFiles(reader);
        var companions = FindAssetCompanionFiles(assetPath, files);
        var assetFiles = new[] { assetPath }.Concat(companions).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in assetFiles)
        {
            var relative = SafeAssetRelativePath(filePath);
            var output = Path.GetFullPath(Path.Combine(destination, relative));
            var rootFull = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!output.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The asset path is not safe to extract.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            ExtractAssetEntry(reader, pakStream, filePath, output);
        }
    }

    private static List<string> GetReaderFiles(object reader)
    {
        var property = reader.GetType().GetProperty("Files", BindingFlags.Public | BindingFlags.Instance);
        var value = property?.GetValue(reader) as System.Collections.IEnumerable;
        if (value == null) return new List<string>();
        return value.Cast<object>().Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    private static void ExtractAssetEntry(object reader, Stream pakStream, string entryPath, string destination)
    {
        var method = reader.GetType().GetMethod("ReadFile", BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(string), typeof(Stream), typeof(Stream) }, null);
        if (method == null) throw new MissingMethodException("The bundled PAK reader does not expose file extraction.");
        using var output = File.Create(destination);
        method.Invoke(reader, new object[] { entryPath, pakStream, output });
    }

    private static void ExtractAssetEntry(string pakPath, string entryPath, string destination)
    {
        using var pakStream = File.OpenRead(pakPath);
        var reader = CreateAssetPakReader(pakStream);
        ExtractAssetEntry(reader, pakStream, entryPath, destination);
    }

        private sealed class AssetWorkshopMeshInfo
        {
            public string AssetName { get; init; } = string.Empty;
            public string PackagePath { get; init; } = string.Empty;
            public long PackageSize { get; init; }
            public string Status { get; init; } = "Static Mesh";
        }

        private static AssetWorkshopMeshInfo BuildStaticMeshInfo(string assetName, string packagePath, long packageSize)
        {
            return new AssetWorkshopMeshInfo
            {
                AssetName = assetName ?? string.Empty,
                PackagePath = packagePath ?? string.Empty,
                PackageSize = packageSize,
                Status = "Static Mesh"
            };
        }

    private static string UnwrapAssetWorkshopException(Exception ex)
    {
        var current = ex;
        while (current is TargetInvocationException tie && tie.InnerException != null)
            current = tie.InnerException;

        while (current.InnerException != null &&
               (current is TypeInitializationException ||
                current is AggregateException ||
                current is TargetInvocationException))
        {
            current = current.InnerException;
        }

        return string.IsNullOrWhiteSpace(current.Message)
            ? current.GetType().Name
            : current.Message;
    }

}
