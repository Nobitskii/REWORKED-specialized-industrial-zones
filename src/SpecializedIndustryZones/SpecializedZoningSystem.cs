using Colossal.Logging;
using Colossal.PSI.Environment;
using Game;
using Game.Economy;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI.InGame;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Entities;

namespace SpecializedIndustryZones;

internal partial class SpecializedZoningSystem : GameSystemBase
{
    private readonly ILog _log = Mod.log;

    private readonly Dictionary<ZonePrefab, BuildingPrefab[]> _spawnableBuildingsByZone = [];
    private readonly Dictionary<string, ZonePrefab> _provisionedZones = [];
    private readonly List<(SpecializedZoneSpec Spec, Entity BaseZone, Entity Zone)> _pendingLabels = [];

    private PrefabSystem _prefabSystem = default!;
    private PrefabUISystem _prefabUISystem = default!;

    private List<PrefabBase> _allPrefabs = default!;
    private ZonePrefab[] _initialZones = default!;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Formatting = Formatting.Indented,
        Converters = [new StringEnumConverter()]
    };

    private DateTimeOffset _lastFileCheck;
    private DateTimeOffset _lastFileModifiedTimestamp;

    // New folder name so this does not clash with the original mod's config file.
    private static readonly string ZoneFilePath = Path.Combine(
        EnvPath.kUserDataPath, "ModsData", "ImprovedSpecializedZones", "SpecializedZones.json");

    protected override void OnCreate()
    {
        base.OnCreate();

        _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        _prefabUISystem = World.GetOrCreateSystemManaged<PrefabUISystem>();

        _allPrefabs = typeof(PrefabSystem)
            .GetField("m_Prefabs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(_prefabSystem) as List<PrefabBase>
            ?? throw new Exception("Could not access m_Prefabs field in PrefabSystem, likely broken by game update");

        // ONE pass over all prefabs. The old code did (zones x prefabs x components) work here.
        var sw = Stopwatch.StartNew();
        var zones = new List<ZonePrefab>();
        var buildingsByZone = new Dictionary<ZonePrefab, List<BuildingPrefab>>();
        var seenZonesForBuilding = new HashSet<ZonePrefab>();

        foreach (var prefab in _allPrefabs)
        {
            if (prefab is ZonePrefab zonePrefab)
            {
                zones.Add(zonePrefab);
            }
            else if (prefab is BuildingPrefab building)
            {
                seenZonesForBuilding.Clear();
                foreach (var component in building.components)
                {
                    if (component is SpawnableBuilding spawnable &&
                        spawnable.m_ZoneType != null &&
                        seenZonesForBuilding.Add(spawnable.m_ZoneType))
                    {
                        if (!buildingsByZone.TryGetValue(spawnable.m_ZoneType, out var list))
                        {
                            list = [];
                            buildingsByZone[spawnable.m_ZoneType] = list;
                        }

                        list.Add(building);
                    }
                }
            }
        }

        _initialZones = [.. zones];
        foreach (var (zone, list) in buildingsByZone)
        {
            _spawnableBuildingsByZone[zone] = [.. list];
        }

        sw.Stop();
        var zonesText = string.Join('\n', _initialZones.Select(z => z.name));
        _log.InfoFormat("Scanned {0} prefabs, found {1} zones in {2} ms:\n{3}",
            _allPrefabs.Count, _initialZones.Length, sw.ElapsedMilliseconds, zonesText);
    }

    protected override void OnUpdate()
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastFileCheck).TotalSeconds < 5)
        {
            return;
        }

        _lastFileCheck = now;

        if (!File.Exists(ZoneFilePath))
        {
            var defaultSpecs = new SpecializedZoneSpecFile();
            foreach (var (specID, spec) in GenerateDefaultSpecs())
            {
                defaultSpecs.Zones[specID] = spec;
            }

            SaveZoneFile(defaultSpecs);
        }

        var lastModified = File.GetLastWriteTimeUtc(ZoneFilePath);

        if (lastModified == _lastFileModifiedTimestamp)
        {
            _log.Trace("No changes detected in zone file, skipping update.");
            return;
        }

        _lastFileModifiedTimestamp = lastModified;
        _log.InfoFormat("Reloading specialized zones from file '{0}'.", ZoneFilePath);
        var sw = Stopwatch.StartNew();

        var specs = LoadZoneFile();
        if (specs == null)
        {
            _log.Warn("No specs loaded, cannot provision zones.");
            return;
        }

        if (specs.UpgradeIfNeeded())
        {
            var added = AddMissingVariantZones(specs);
            SaveZoneFile(specs);
            // Saving changes the file timestamp; record it so we don't reload everything again 5s later.
            _lastFileModifiedTimestamp = File.GetLastWriteTimeUtc(ZoneFilePath);
            _log.Info($"Upgraded specialized zone specs to version {specs.Version}, added {added} variant zone(s).");
        }

        var numLoaded = 0;
        var totalBuildings = 0;
        foreach (var (specID, spec) in specs.Zones)
        {
            try
            {
                var zoneSw = Stopwatch.StartNew();

                var baseZone = _initialZones.FirstOrDefault(z => z.name == spec.BaseZoneName)
                    ?? throw new Exception($"Base zone '{spec.BaseZoneName}' not found for specialized zone '{spec.Name}'");
                var combinedFilter = spec.CombinedFilter;

                var existing = _provisionedZones.ContainsKey(specID);
                var zone = CreateZonePrefab(specID, spec, baseZone, combinedFilter);
                if (zone == null)
                {
                    _log.Error($"Failed to create zone prefab: {specID}");
                    continue;
                }

                if (existing)
                {
                    _prefabSystem.UpdatePrefab(zone);
                }
                else if (!_prefabSystem.AddPrefab(zone))
                {
                    _log.Error($"Failed to provision zone: {specID}");
                    continue;
                }

                var clonedBuildings = 0;
                foreach (var building in CopyBuildings(specID, spec, zone, baseZone, combinedFilter))
                {
                    try
                    {
                        if (existing)
                        {
                            _prefabSystem.UpdatePrefab(building);
                            clonedBuildings++;
                        }
                        else if (_prefabSystem.AddPrefab(building))
                        {
                            clonedBuildings++;
                        }
                        else
                        {
                            _log.Error($"Failed to add specialized building prefab {building.name} to PrefabSystem. It may already exist or be invalid.");
                        }
                    }
                    catch (Exception e)
                    {
                        _log.Error($"Failed to add building prefab {building.name}: {e}");
                    }
                }

                QueueUILabels(spec, baseZone, zone);

                _provisionedZones[specID] = zone;
                numLoaded++;
                totalBuildings += clonedBuildings;

                zoneSw.Stop();
                _log.InfoFormat("Zone {0}: {1} building prefabs in {2} ms.", specID, clonedBuildings, zoneSw.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
                _log.Error(e, $"Failed to provision zone {spec.Name}");
                continue;
            }
        }

        RegisterUILabels();

        sw.Stop();
        _log.InfoFormat("{0} specialized zones ({1} building prefabs) reloaded in {2} ms.",
            numLoaded, totalBuildings, sw.ElapsedMilliseconds);
    }

    private SpecializedZoneSpecFile? LoadZoneFile()
    {
        if (!File.Exists(ZoneFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(ZoneFilePath);
            return JsonConvert.DeserializeObject<SpecializedZoneSpecFile>(json, JsonSettings);
        }
        catch (Exception e)
        {
            Mod.log.Error(e, "Failed to load specialized zones from file.");
            return null;
        }
    }

    private void SaveZoneFile(SpecializedZoneSpecFile specs)
    {
        try
        {
            var directory = Path.GetDirectoryName(ZoneFilePath) ?? throw new Exception("Zone file directory is null");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(specs, JsonSettings);
            File.WriteAllText(ZoneFilePath, json);
            _log.Info("Specialized zones saved to file.");
        }
        catch (Exception e)
        {
            _log.Error(e, "Failed to save specialized zones to file.");
        }
    }

    // Used once when an older config file is upgraded: adds variant zones it doesn't have yet.
    // Zones already in the file (including ones the user edited) are left alone.
    private int AddMissingVariantZones(SpecializedZoneSpecFile specs)
    {
        var added = 0;
        foreach (var (specID, spec) in GenerateDefaultSpecs())
        {
            if (specID.EndsWith("General") || specs.Zones.ContainsKey(specID))
            {
                continue;
            }

            specs.Zones[specID] = spec;
            added++;
        }

        return added;
    }

    private IEnumerable<(string, SpecializedZoneSpec)> GenerateDefaultSpecs()
    {
        foreach (var zone in SpecializedZoneSpecSource.AllZones)
        {
            if (zone.Type == IndustryType.Warehouses && !Mod.IncludeWarehousesOnlyZones)
            {
                continue;
            }

            if (zone.Type == IndustryType.Manufacturing && !Mod.IncludeManufacturingOnlyZones)
            {
                continue;
            }

            var prefix = zone.Type switch
            {
                IndustryType.General => "Manufacturing and warehouses",
                IndustryType.Manufacturing => "Manufacturing only",
                IndustryType.Warehouses => "Warehouses only",
                _ => "Unknown"
            };

            var includedResources = string.Join(", ", zone.Filter);
            var description = $"{prefix} - {includedResources}";

            var iconBase = zone.Type switch
            {
                IndustryType.General or IndustryType.Manufacturing => "ZoneIndustrialManufacturing",
                IndustryType.Warehouses => "ZoneIndustrialWarehouses",
                _ => throw new ArgumentOutOfRangeException(nameof(zone.Type), zone.Type, "Invalid type")
            };

            var iconUri = $"coui://{Mod.HostName}/{iconBase}_{zone.Specialization}.svg";

            var spec = new SpecializedZoneSpec(zone.Name, "Industrial Manufacturing", description)
            {
                Color = zone.Color,
                IconUri = iconUri,
                Filters =
                [
                    new SpecializedZoneFilterSpec
                    {
                        ManufacturedResources = zone.Type == IndustryType.Warehouses ? [] : zone.Filter.ToHashSet(),
                        StoredResources = zone.Filter.ToHashSet(),
                        SoldResources = new HashSet<ResourceInEditor>(),
                        RequireManufactured = zone.Type == IndustryType.Manufacturing ? true : null
                    }
                ]
            };

            yield return (zone.ID, spec);
        }
    }

    private ZonePrefab? CreateZonePrefab(
        string specID,
        SpecializedZoneSpec spec,
        ZonePrefab sourceZone,
        SpecializedZoneFilterSpec combinedFilter)
    {
        var zone = Clone(specID, sourceZone, spec);
        zone.m_Edge = spec.Color;

        if (zone.name == sourceZone.name)
        {
            _log.Warn($"Zone {sourceZone.name} was not renamed for specialization {specID} and will be ignored.");
            return null;
        }

        var zoneProps = zone.GetComponent<ZoneProperties>();

        if (combinedFilter.ManufacturedResources != null)
            zoneProps.m_AllowedManufactured = [.. combinedFilter.ManufacturedResources.Intersect(zoneProps.m_AllowedManufactured)];
        if (combinedFilter.StoredResources != null)
            zoneProps.m_AllowedStored = [.. combinedFilter.StoredResources.Intersect(zoneProps.m_AllowedStored)];
        if (combinedFilter.SoldResources != null)
            zoneProps.m_AllowedSold = [.. combinedFilter.SoldResources.Intersect(zoneProps.m_AllowedSold)];

        if (spec.IconUri != null)
        {
            var uiObj = zone.GetComponent<UIObject>();
            uiObj.m_Icon = spec.IconUri;
        }

        HandleObsoleteIdentifiers(specID, sourceZone, zone, spec);

        return zone;
    }

    private static bool Overlaps(ResourceInEditor[]? source, HashSet<ResourceInEditor> filter)
    {
        if (source == null || filter.Count == 0) return false;
        foreach (var r in source)
        {
            if (filter.Contains(r)) return true;
        }

        return false;
    }

    private IEnumerable<BuildingPrefab> CopyBuildings(
        string specID,
        SpecializedZoneSpec spec,
        ZonePrefab zone,
        ZonePrefab sourceZone,
        SpecializedZoneFilterSpec combinedFilter)
    {
        if (!_spawnableBuildingsByZone.TryGetValue(sourceZone, out var buildings))
        {
            _log.Error($"No spawnable buildings found for zone {sourceZone.name}");
            yield break;
        }

        // Null filters are treated as empty here (the original threw on null).
        var manufacturedFilter = new HashSet<ResourceInEditor>(combinedFilter.ManufacturedResources ?? Enumerable.Empty<ResourceInEditor>());
        var soldFilter = new HashSet<ResourceInEditor>(combinedFilter.SoldResources ?? Enumerable.Empty<ResourceInEditor>());
        var storedFilter = new HashSet<ResourceInEditor>(combinedFilter.StoredResources ?? Enumerable.Empty<ResourceInEditor>());

        foreach (var sourceBuilding in buildings)
        {
            if (sourceBuilding.TryGet<BuildingProperties>(out var sourceBuildingProperties))
            {
                var hasRelevantManufactured = Overlaps(sourceBuildingProperties.m_AllowedManufactured, manufacturedFilter);
                var hasRelevantSold = Overlaps(sourceBuildingProperties.m_AllowedSold, soldFilter);
                var hasRelevantStored = Overlaps(sourceBuildingProperties.m_AllowedStored, storedFilter);

                if (!hasRelevantManufactured && !hasRelevantSold && !hasRelevantStored)
                {
                    // Not applicable to this specialization
                    continue;
                }

                if (combinedFilter.RequireManufactured == true && !hasRelevantManufactured)
                {
                    continue;
                }

                if (combinedFilter.RequireSold == true && !hasRelevantSold)
                {
                    continue;
                }

                if (combinedFilter.RequireStored == true && !hasRelevantStored)
                {
                    continue;
                }
            }

            var building = Clone(specID, sourceBuilding, spec);

            if (building.name == sourceBuilding.name)
            {
                _log.Warn($"Building {sourceBuilding.name} was not renamed for specialization {specID} and will be ignored.");
                continue;
            }

            var spawnableBuilding = building.GetComponent<SpawnableBuilding>();
            spawnableBuilding.m_ZoneType = zone;

            if (building.TryGet<BuildingProperties>(out var buildingProperties))
            {
                if (combinedFilter.ManufacturedResources != null)
                    buildingProperties.m_AllowedManufactured = [.. combinedFilter.ManufacturedResources.Intersect(buildingProperties.m_AllowedManufactured)];
                if (combinedFilter.StoredResources != null)
                    buildingProperties.m_AllowedStored = [.. combinedFilter.StoredResources.Intersect(buildingProperties.m_AllowedStored)];
                if (combinedFilter.SoldResources != null)
                    buildingProperties.m_AllowedSold = [.. combinedFilter.SoldResources.Intersect(buildingProperties.m_AllowedSold)];
            }

            HandleObsoleteIdentifiers(specID, sourceBuilding, building, spec);

            yield return building;
        }
    }

    // Records label info per zone; actual registration happens once per reload in RegisterUILabels.
    private void QueueUILabels(SpecializedZoneSpec spec, ZonePrefab industrialZone, ZonePrefab zone)
    {
        if (!_prefabSystem.TryGetEntity(zone, out var zoneEntity))
        {
            _log.Error($"Could not find entity for zone prefab {zone.name}");
            return;
        }

        if (!_prefabSystem.TryGetEntity(industrialZone, out var industrialZoneEntity))
        {
            _log.Error($"Could not find entity for industrial zone prefab {industrialZone.name}");
            return;
        }

        _pendingLabels.Add((spec, industrialZoneEntity, zoneEntity));
    }

    private void RegisterUILabels()
    {
        if (_pendingLabels.Count == 0) return;

        var source = new SpecializedIndustrialZoneLocaleSource(_prefabUISystem, _log);
        foreach (var (spec, baseZone, zone) in _pendingLabels)
        {
            source.Add(spec, baseZone, zone);
        }

        _pendingLabels.Clear();

        // One source per language, instead of one per zone per language.
        foreach (var localeId in GameManager.instance.localizationManager.GetSupportedLocales())
        {
            GameManager.instance.localizationManager.AddSource(localeId, source);
        }
    }

    private static T Clone<T>(string specID, T source, SpecializedZoneSpec spec)
        where T : PrefabBase
    {
        var newName = GetUpdatedName(specID, source.name, spec);
        var clone = (T)source.Clone(newName);
        return clone;
    }

    private static string GetUpdatedName(string specID, string originalName, SpecializedZoneSpec spec)
    {
        if (originalName.Contains("IndustrialManufacturing"))
        {
            return originalName.Replace("IndustrialManufacturing", $"SpecializedIndustrialManufacturing{specID}");
        }

        if (originalName.Contains("Industrial Manufacturing"))
        {
            return originalName.Replace("Industrial Manufacturing", $"SpecializedIndustrialManufacturing{specID}");
        }

        if (originalName.Contains("IndustrialStorage"))
        {
            return originalName.Replace("IndustrialStorage", $"SpecializedIndustrialStorage{specID}");
        }

        if (originalName.Contains("Warehouses"))
        {
            return originalName.Replace("Warehouses", $"SpecializedIndustrialStorage{specID}");
        }

        if (originalName.Contains("Industrial"))
        {
            return originalName.Replace("Industrial", $"SpecializedIndustrial{specID}");
        }

        return originalName + specID;
    }

    private void HandleObsoleteIdentifiers(string specID, PrefabBase sourcePrefab, PrefabBase newPrefab, SpecializedZoneSpec spec)
    {
        var obsoleteIdentifiers = newPrefab.AddOrGetComponent<ObsoleteIdentifiers>();

        // This is required due to the rename in 0.2.1, may be removed in future versions
        var legacyName = sourcePrefab.name.Replace("Industrial", $"Specialized Industrial {spec.Name}");
        var legacyPrefabInfo = new PrefabIdentifierInfo
        {
            m_Name = legacyName,
            m_Type = newPrefab.GetType().Name
        };

        if (obsoleteIdentifiers.m_PrefabIdentifiers == null)
        {
            obsoleteIdentifiers.m_PrefabIdentifiers = [legacyPrefabInfo];
        }
        else
        {
            var extraLegacyInfos = new List<PrefabIdentifierInfo>();
            foreach (var identifier in obsoleteIdentifiers.m_PrefabIdentifiers)
            {
                var newName = GetUpdatedName(specID, identifier.m_Name, spec);
                if (newName == identifier.m_Name)
                    _log.Warn($"Identifier {identifier.m_Name} for prefab {sourcePrefab.name} was not updated for specialization {specID}.");

                identifier.m_Name = newName;
                extraLegacyInfos.Add(new PrefabIdentifierInfo
                {
                    m_Name = identifier.m_Name.Replace("Industrial", $"Specialized Industrial {spec.Name}"),
                    m_Type = identifier.m_Type
                });
            }

            obsoleteIdentifiers.m_PrefabIdentifiers =
            [
                ..obsoleteIdentifiers.m_PrefabIdentifiers,
                legacyPrefabInfo,
                ..extraLegacyInfos
            ];
        }
    }
}
