using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.IO;
using ThermalGuard.Data;
using ThermalGuard.Data.Entities;
using ThermalGuard.Models;
using System.Globalization;
using System.Text;
using DataCamera = ThermalGuard.Data.Entities.Camera;
using ModelCamera = ThermalGuard.Models.Camera;

namespace ThermalGuard.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private readonly string _databasePath;

    public DatabaseService()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDirectory);
        _databasePath = Path.Combine(dataDirectory, "nvr.db");
        _connectionString = $"Data Source={_databasePath}";
    }

    public async Task ExportAsync(string destinationPath, string entityName)
    {
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var db = CreateContext();
        await using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write,
            FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        switch (entityName)
        {
            case "camera":
                await ExportTableAsync(db, writer, db.Cameras);
                break;
            case "recording_setting":
                await ExportTableAsync(db, writer, db.RecordingSettings);
                break;
            case "region_alarm_setting":
                await ExportRegionAlarmTableAsync(db, writer);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entityName), "不支援的 Entity");
        }
    }

    public async Task<string> ImportAsync(string sourcePath, string entityName)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("找不到匯入設定檔", source);

        var backup = $"{_databasePath}.bak-before-import-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Copy(_databasePath, backup, false);
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        switch (entityName)
        {
            case "camera":
                var existing = await db.Cameras.ToDictionaryAsync(item => item.Id,
                    StringComparer.OrdinalIgnoreCase);
                var imported = await ReadTableAsync<DataCamera>(db, source);
                if (imported.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                    imported.Count)
                    throw new InvalidDataException("Camera CSV 包含重複的 Id");
                for (var index = 0; index < imported.Count; index++)
                    imported[index].SortOrder = index + 1;

                var untouched = existing.Values
                    .Where(item => imported.All(value =>
                        !value.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
                    .Select(item => (Camera: item, OriginalOrder: item.SortOrder))
                    .OrderBy(item => item.OriginalOrder)
                    .ToArray();
                var temporaryOrder = -1;
                foreach (var camera in existing.Values)
                    camera.SortOrder = temporaryOrder--;
                await db.SaveChangesAsync();

                foreach (var camera in imported)
                {
                    if (existing.TryGetValue(camera.Id, out var current))
                        db.Entry(current).CurrentValues.SetValues(camera);
                    else
                        db.Cameras.Add(camera);
                }
                var nextOrder = imported.Count + 1;
                foreach (var (camera, _) in untouched)
                    camera.SortOrder = nextOrder++;
                break;
            case "recording_setting":
                var recordingSettings = await db.RecordingSettings
                    .ToDictionaryAsync(item => item.Id);
                foreach (var setting in await ReadTableAsync<RecordingSetting>(db, source))
                {
                    if (recordingSettings.TryGetValue(setting.Id, out var current))
                        db.Entry(current).CurrentValues.SetValues(setting);
                    else
                        db.RecordingSettings.Add(setting);
                }
                break;
            case "region_alarm_setting":
                var regionAlarmSettings = await db.RegionAlarmSettings
                    .ToDictionaryAsync(item => (item.CameraId, item.RegionNumber));
                foreach (var setting in await ReadRegionAlarmTableAsync(source))
                {
                    if (regionAlarmSettings.TryGetValue(
                            (setting.CameraId, setting.RegionNumber), out var current))
                    {
                        db.Entry(current.InternalWarning).CurrentValues
                            .SetValues(setting.InternalWarning);
                        db.Entry(current.InternalAlarm).CurrentValues
                            .SetValues(setting.InternalAlarm);
                        db.Entry(current.InternalRise).CurrentValues
                            .SetValues(setting.InternalRise);
                        db.Entry(current.ThermalWarning).CurrentValues
                            .SetValues(setting.ThermalWarning);
                        db.Entry(current.ThermalAlarm).CurrentValues
                            .SetValues(setting.ThermalAlarm);
                    }
                    else
                    {
                        db.RegionAlarmSettings.Add(setting);
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entityName), "不支援的 Entity");
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return backup;
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quoted && ch == '"' && index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; }
            else if (ch == '"') quoted = !quoted;
            else if (!quoted && ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (!quoted && (ch == '\r' || ch == '\n'))
            {
                if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString()); field.Clear(); if (row.Any(value => value.Length > 0)) rows.Add(row); row = [];
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static async Task ExportTableAsync<TEntity>(ThermalGuardDbContext db, TextWriter writer,
        IQueryable<TEntity> query) where TEntity : class
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity))!;
        var table = entityType.GetTableName()!;
        var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        var properties = entityType.GetProperties().Where(property => property.PropertyInfo is not null).ToArray();
        await writer.WriteLineAsync(string.Join(',', properties.Select(property =>
            Csv(property.GetColumnName(store)!))));
        foreach (var entity in await query.AsNoTracking().ToListAsync())
            await writer.WriteLineAsync(string.Join(',', properties.Select(property =>
                Csv(Convert.ToString(property.PropertyInfo!.GetValue(entity),
                    CultureInfo.InvariantCulture) ?? ""))));
    }

    private static async Task<List<TEntity>> ReadTableAsync<TEntity>(ThermalGuardDbContext db,
        string filePath) where TEntity : class, new()
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity))!;
        var table = entityType.GetTableName()!;
        var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        var properties = entityType.GetProperties().Where(property => property.PropertyInfo is not null).ToArray();
        var columns = properties.Select(property => property.GetColumnName(store)!).ToArray();
        var rows = ParseCsv(await File.ReadAllTextAsync(filePath));
        if (rows.Count == 0 || !rows[0].SequenceEqual(columns))
            throw new InvalidDataException($"{table}.csv 的欄位名稱或順序不正確");
        if (rows.Skip(1).Any(row => row.Count != columns.Length))
            throw new InvalidDataException($"{table}.csv 的資料欄位數不正確");
        return rows.Skip(1).Select(row =>
        {
            var entity = new TEntity();
            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index].PropertyInfo!;
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                object? value = targetType == typeof(string) ? row[index] :
                    targetType.IsEnum ? Enum.Parse(targetType, row[index]) :
                    Convert.ChangeType(row[index], targetType, CultureInfo.InvariantCulture);
                property.SetValue(entity, value);
            }
            return entity;
        }).ToList();
    }

    private sealed record AlarmColumn(string Name, Func<RegionAlarmSetting, object> Get,
        Action<RegionAlarmSetting, string> Set);

    private static readonly AlarmColumn[] RegionAlarmFields = BuildRegionAlarmFields();

    private static AlarmColumn[] BuildRegionAlarmFields()
    {
        var fields = new List<AlarmColumn>();
        AddInternal("internal_warning", item => item.InternalWarning);
        AddInternal("internal_alarm", item => item.InternalAlarm);
        AddInternal("internal_rise", item => item.InternalRise);
        fields.Add(new("internal_rise_window_seconds", item => item.InternalRise.RiseWindowSeconds,
            (item, value) => item.InternalRise.RiseWindowSeconds = int.Parse(value, CultureInfo.InvariantCulture)));
        AddAction("thermal_warning", item => item.ThermalWarning);
        AddAction("thermal_alarm", item => item.ThermalAlarm);
        return fields.ToArray();

        void AddInternal(string prefix, Func<RegionAlarmSetting, InternalEventProfile> get)
        {
            AddAction(prefix, get);
            fields.Add(new($"{prefix}_trigger_value", item => get(item).TriggerValue,
                (item, value) => get(item).TriggerValue = double.Parse(value, CultureInfo.InvariantCulture)));
            fields.Add(new($"{prefix}_confirm_cycles", item => get(item).ConfirmCycles,
                (item, value) => get(item).ConfirmCycles = int.Parse(value, CultureInfo.InvariantCulture)));
            fields.Add(new($"{prefix}_deadband", item => get(item).Deadband,
                (item, value) => get(item).Deadband = double.Parse(value, CultureInfo.InvariantCulture)));
        }

        void AddAction(string prefix, Func<RegionAlarmSetting, EventActionProfile> get)
        {
            fields.Add(new($"{prefix}_enabled", item => get(item).Enabled,
                (item, value) => get(item).Enabled = bool.Parse(value)));
            fields.Add(new($"{prefix}_show_popup", item => get(item).ShowPopup,
                (item, value) => get(item).ShowPopup = bool.Parse(value)));
            fields.Add(new($"{prefix}_play_alarm_sound", item => get(item).PlayAlarmSound,
                (item, value) => get(item).PlayAlarmSound = bool.Parse(value)));
            fields.Add(new($"{prefix}_alarm_sound", item => get(item).AlarmSound,
                (item, value) => get(item).AlarmSound = value));
            fields.Add(new($"{prefix}_retention_mode", item => get(item).RetentionMode,
                (item, value) => get(item).RetentionMode = value));
            fields.Add(new($"{prefix}_duration_seconds", item => get(item).DurationSeconds,
                (item, value) => get(item).DurationSeconds = int.Parse(value, CultureInfo.InvariantCulture)));
        }
    }

    private static string[] RegionAlarmColumns() =>
        ["camera_id", "region_number", .. RegionAlarmFields.Select(field => field.Name)];

    private static async Task ExportRegionAlarmTableAsync(ThermalGuardDbContext db, TextWriter writer)
    {
        var columns = RegionAlarmColumns();
        await writer.WriteLineAsync(string.Join(',', columns.Select(Csv)));
        foreach (var entity in await db.RegionAlarmSettings.AsNoTracking()
                     .OrderBy(item => item.CameraId).ThenBy(item => item.RegionNumber).ToListAsync())
        {
            var values = new List<string> { entity.CameraId, entity.RegionNumber.ToString(CultureInfo.InvariantCulture) };
            foreach (var field in RegionAlarmFields)
                values.Add(Convert.ToString(field.Get(entity), CultureInfo.InvariantCulture) ?? "");
            await writer.WriteLineAsync(string.Join(',', values.Select(Csv)));
        }
    }

    private static async Task<List<RegionAlarmSetting>> ReadRegionAlarmTableAsync(string filePath)
    {
        var columns = RegionAlarmColumns();
        var rows = ParseCsv(await File.ReadAllTextAsync(filePath));
        if (rows.Count == 0 || !rows[0].SequenceEqual(columns) ||
            rows.Skip(1).Any(row => row.Count != columns.Length))
            throw new InvalidDataException("region_alarm_setting.csv 的欄位不正確");
        return rows.Skip(1).Select(row =>
        {
            var entity = new RegionAlarmSetting
            {
                CameraId = row[0],
                RegionNumber = int.Parse(row[1], CultureInfo.InvariantCulture)
            };
            var index = 2;
            foreach (var field in RegionAlarmFields)
                field.Set(entity, row[index++]);
            return entity;
        }).ToList();
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task<List<ModelCamera>> LoadCamerasAsync()
    {
        await using var db = CreateContext();
        return await db.Cameras
            .AsNoTracking()
            .OrderBy(camera => camera.SortOrder)
            .Select(camera => new ModelCamera
            {
                Id = camera.Id,
                Name = camera.Name,
                Rtsp = camera.Rtsp,
                SourceType = camera.SourceType,
                ImagingType = camera.ImagingType,
                StreamType = camera.StreamType,
                SortOrder = camera.SortOrder
            })
            .ToListAsync();
    }

    public async Task AddCameraAsync(ModelCamera camera)
    {
        await using var db = CreateContext();
        db.Cameras.Add(new DataCamera
        {
            Id = camera.Id,
            Name = camera.Name,
            Rtsp = camera.Rtsp,
            SourceType = camera.SourceType,
            ImagingType = camera.ImagingType,
            StreamType = camera.StreamType,
            SortOrder = camera.SortOrder
        });
        await db.SaveChangesAsync();
    }

    public async Task DeleteCameraAsync(string id)
    {
        await using var db = CreateContext();
        var camera = await db.Cameras.SingleAsync(item => item.Id == id);
        db.Cameras.Remove(camera);
        await db.SaveChangesAsync();
    }

    public async Task UpdateCameraAsync(ModelCamera camera)
    {
        await using var db = CreateContext();
        var entity = await db.Cameras.SingleAsync(item => item.Id == camera.Id);
        entity.Name = camera.Name;
        entity.Rtsp = camera.Rtsp;
        entity.SourceType = camera.SourceType;
        entity.ImagingType = camera.ImagingType;
        entity.StreamType = camera.StreamType;
        await db.SaveChangesAsync();
    }

    public async Task<RecordingSettings> LoadRecordingAsync()
    {
        await using var db = CreateContext();
        var setting = await db.RecordingSettings.SingleOrDefaultAsync(item => item.Id == 1);
        if (setting is null)
            return new(false, 5, 3, 10, Path.Combine(AppContext.BaseDirectory, "recordings"), 10, true);

        var currentDirectory = Path.Combine(AppContext.BaseDirectory, "recordings");
        if (IsPreviousBuildRecordingDirectory(setting.OutputDirectory, currentDirectory))
        {
            setting.OutputDirectory = currentDirectory;
            await db.SaveChangesAsync();
        }
        return new(setting.Enabled, setting.SegmentSeconds, setting.BufferMinutes,
            setting.PostEventSeconds, setting.OutputDirectory, setting.ReservedSpaceGb,
            setting.OverwriteOldestEvents);
    }

    public async Task SaveRecordingAsync(RecordingSettings settings)
    {
        await using var db = CreateContext();
        var entity = await db.RecordingSettings.SingleOrDefaultAsync(item => item.Id == 1);
        if (entity is null)
        {
            entity = new RecordingSetting { Id = 1 };
            db.RecordingSettings.Add(entity);
        }
        entity.Enabled = settings.Enabled;
        entity.SegmentSeconds = settings.SegmentSeconds;
        entity.BufferMinutes = settings.BufferMinutes;
        entity.PostEventSeconds = settings.PostEventSeconds;
        entity.OutputDirectory = settings.OutputDirectory;
        entity.ReservedSpaceGb = settings.ReservedSpaceGb;
        entity.OverwriteOldestEvents = settings.OverwriteOldestEvents;
        await db.SaveChangesAsync();
    }

    public async Task<List<EventSettings>> LoadEventSettingsAsync()
    {
        await using var db = CreateContext();
        return await db.EventSettings.AsNoTracking()
            .Select(setting => new EventSettings(setting.CameraId, setting.Enabled,
                setting.WarningTemperature, setting.AlarmTemperature, setting.TriggerSeconds,
                setting.TemperatureRise, setting.RiseWindowSeconds,
                setting.HighTemperatureConfirmCycle, setting.HighTemperatureDeadband,
                setting.RiseConfirmCycle, setting.RiseDeadband,
                setting.ShowPopup, setting.PlayAlarmSound, setting.AlarmSound,
                setting.ThermalWarningRegionMask, setting.ThermalAlarmRegionMask,
                setting.InternalWarningRegionMask, setting.InternalAlarmRegionMask, setting.InternalRiseRegionMask))
            .ToListAsync();
    }

    public async Task SaveEventSettingsAsync(IReadOnlyCollection<EventSettings> settings)
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var existing = await db.EventSettings.ToDictionaryAsync(item => item.CameraId,
            StringComparer.OrdinalIgnoreCase);
        var regions = await db.TemperatureRegionSettings
            .ToDictionaryAsync(item => (item.CameraId, item.RegionNumber));
        foreach (var setting in settings)
        {
            if (!existing.TryGetValue(setting.CameraId, out var entity))
            {
                entity = new EventSetting { CameraId = setting.CameraId };
                db.EventSettings.Add(entity);
            }
            entity.Enabled = setting.Enabled;
            entity.WarningTemperature = setting.WarningTemperature;
            entity.AlarmTemperature = setting.AlarmTemperature;
            entity.TriggerSeconds = setting.TriggerSeconds;
            entity.TemperatureRise = setting.TemperatureRise;
            entity.RiseWindowSeconds = setting.RiseWindowSeconds;
            entity.HighTemperatureConfirmCycle = setting.HighTemperatureConfirmCycle;
            entity.HighTemperatureDeadband = setting.HighTemperatureDeadband;
            entity.RiseConfirmCycle = setting.RiseConfirmCycle;
            entity.RiseDeadband = setting.RiseDeadband;
            entity.ShowPopup = setting.ShowPopup;
            entity.PlayAlarmSound = setting.PlayAlarmSound;
            entity.AlarmSound = setting.AlarmSound;
            entity.ThermalWarningRegionMask = setting.ThermalWarningRegionMask;
            entity.ThermalAlarmRegionMask = setting.ThermalAlarmRegionMask;
            entity.InternalWarningRegionMask = setting.InternalWarningRegionMask;
            entity.InternalAlarmRegionMask = setting.InternalAlarmRegionMask;
            entity.InternalRiseRegionMask = setting.InternalRiseRegionMask;
            for (var number = 1; number <= 21; number++)
            {
                if (!regions.TryGetValue((setting.CameraId, number), out var region))
                {
                    region = new TemperatureRegionSetting { CameraId = setting.CameraId, RegionNumber = number };
                    db.TemperatureRegionSettings.Add(region);
                    regions[(setting.CameraId, number)] = region;
                }
                var bit = 1 << (number - 1);
                region.InternalWarningEnabled = (setting.InternalWarningRegionMask & bit) != 0;
                region.InternalAlarmEnabled = (setting.InternalAlarmRegionMask & bit) != 0;
                region.InternalRiseEnabled = (setting.InternalRiseRegionMask & bit) != 0;
                region.ThermalWarningEnabled = (setting.ThermalWarningRegionMask & bit) != 0;
                region.ThermalAlarmEnabled = (setting.ThermalAlarmRegionMask & bit) != 0;
            }
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<List<RegionAlarmSettings>> LoadRegionAlarmSettingsAsync()
    {
        await using var db = CreateContext();
        var entities = await db.RegionAlarmSettings.AsNoTracking().ToListAsync();
        return entities.SelectMany(ToRegionAlarmSettings).ToList();
    }

    public async Task SaveRegionAlarmSettingsAsync(IReadOnlyCollection<RegionAlarmSettings> settings)
    {
        await using var db = CreateContext();
        var existing = await db.RegionAlarmSettings
            .ToDictionaryAsync(item => (item.CameraId, item.RegionNumber));
        foreach (var setting in settings)
        {
            var key = (setting.CameraId, setting.RegionNumber);
            if (!existing.TryGetValue(key, out var entity))
            {
                entity = new RegionAlarmSetting
                {
                    CameraId = setting.CameraId,
                    RegionNumber = setting.RegionNumber
                };
                db.RegionAlarmSettings.Add(entity);
                existing[key] = entity;
            }
            ApplyRegionAlarmSettings(entity, setting);
        }
        await db.SaveChangesAsync();
    }

    private static IEnumerable<RegionAlarmSettings> ToRegionAlarmSettings(RegionAlarmSetting entity)
    {
        yield return ToInternalSettings(entity, "程式內部預警", entity.InternalWarning, 60);
        yield return ToInternalSettings(entity, "程式內部警報", entity.InternalAlarm, 60);
        yield return ToInternalSettings(entity, "程式內部溫升", entity.InternalRise,
            entity.InternalRise.RiseWindowSeconds);
        yield return ToThermalSettings(entity, "熱像儀預警", entity.ThermalWarning);
        yield return ToThermalSettings(entity, "熱像儀警報", entity.ThermalAlarm);
    }

    private static RegionAlarmSettings ToInternalSettings(RegionAlarmSetting entity,
        string eventType, InternalEventProfile profile, int riseWindowSeconds) =>
        new(entity.CameraId, entity.RegionNumber, eventType, profile.Enabled, profile.TriggerValue, 0,
            profile.ConfirmCycles, profile.Deadband, riseWindowSeconds, profile.ShowPopup,
            profile.PlayAlarmSound, profile.AlarmSound, profile.RetentionMode, profile.DurationSeconds);

    private static RegionAlarmSettings ToThermalSettings(RegionAlarmSetting entity,
        string eventType, ThermalEventProfile profile) => new(entity.CameraId, entity.RegionNumber,
        eventType, profile.Enabled, 0, 0, 1, 0, 60, profile.ShowPopup,
        profile.PlayAlarmSound, profile.AlarmSound, profile.RetentionMode, profile.DurationSeconds);

    private static void ApplyRegionAlarmSettings(RegionAlarmSetting entity,
        RegionAlarmSettings setting)
    {
        EventActionProfile profile = setting.EventType switch
        {
            "程式內部預警" => entity.InternalWarning,
            "程式內部警報" => entity.InternalAlarm,
            "程式內部溫升" => entity.InternalRise,
            "熱像儀預警" => entity.ThermalWarning,
            "熱像儀警報" => entity.ThermalAlarm,
            _ => throw new ArgumentOutOfRangeException(nameof(setting.EventType))
        };
        profile.Enabled = setting.Enabled;
        profile.ShowPopup = setting.ShowPopup;
        profile.PlayAlarmSound = setting.PlayAlarmSound;
        profile.AlarmSound = setting.AlarmSound;
        profile.RetentionMode = setting.RetentionMode;
        profile.DurationSeconds = setting.DurationSeconds;
        if (profile is InternalEventProfile internalProfile)
        {
            internalProfile.TriggerValue = setting.TriggerValue;
            internalProfile.ConfirmCycles = setting.ConfirmCycles;
            internalProfile.Deadband = setting.Deadband;
        }
        if (profile is InternalRiseEventProfile riseProfile)
            riseProfile.RiseWindowSeconds = setting.RiseWindowSeconds;
    }

    public async Task<List<ModbusSettings>> LoadModbusSettingsAsync()
    {
        await using var db = CreateContext();
        return await db.ModbusSettings.AsNoTracking()
            .Select(setting => new ModbusSettings(setting.CameraId, setting.Enabled, setting.Host,
                setting.Port, setting.SlaveId, setting.RegisterAddress, setting.AlarmRegisterAddress,
                setting.AccessMode, setting.PollIntervalMs))
            .ToListAsync();
    }

    public async Task SaveModbusSettingsAsync(IReadOnlyCollection<ModbusSettings> settings)
    {
        await using var db = CreateContext();
        var existing = await db.ModbusSettings.ToDictionaryAsync(item => item.CameraId,
            StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings)
        {
            if (!existing.TryGetValue(setting.CameraId, out var entity))
            {
                entity = new ModbusSetting { CameraId = setting.CameraId };
                db.ModbusSettings.Add(entity);
            }
            entity.Enabled = setting.Enabled;
            entity.Host = setting.Host;
            entity.Port = setting.Port;
            entity.SlaveId = 1;
            entity.RegisterAddress = setting.RegisterAddress;
            entity.AlarmRegisterAddress = setting.AlarmRegisterAddress;
            entity.AccessMode = setting.AccessMode;
            entity.PollIntervalMs = setting.PollIntervalMs;
        }
        await db.SaveChangesAsync();
    }

    public async Task SaveOrderAsync(IReadOnlyList<ModelCamera> cameras)
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var entities = await db.Cameras.ToDictionaryAsync(camera => camera.Id, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < cameras.Count; i++) entities[cameras[i].Id].SortOrder = -(i + 1);
        await db.SaveChangesAsync();

        for (var i = 0; i < cameras.Count; i++)
        {
            entities[cameras[i].Id].SortOrder = i + 1;
            cameras[i].SortOrder = i + 1;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private ThermalGuardDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ThermalGuardDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new ThermalGuardDbContext(options);
    }

    private static bool IsPreviousBuildRecordingDirectory(string storedDirectory, string currentDirectory)
    {
        try
        {
            var stored = Path.GetFullPath(storedDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var current = Path.GetFullPath(currentDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (stored.Equals(current, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(stored).Equals("recordings", StringComparison.OrdinalIgnoreCase)) return false;
            var binPart = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
            return stored.Contains(binPart, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
