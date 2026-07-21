using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
using ThermalGuard.Data;
using ThermalGuard.Data.Entities;
using ThermalGuard.Models;
using System.Globalization;
using System.Text;

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
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task ExportAsync(string destinationPath)
    {
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var writer = new StreamWriter(destination, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("Table,Row,Column,Value");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        foreach (var table in SettingsTablesInInsertOrder)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table}";
            await using var reader = await command.ExecuteReaderAsync();
            var row = 0;
            while (await reader.ReadAsync())
            {
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    var value = reader.IsDBNull(column) ? "" : Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture) ?? "";
                    await writer.WriteLineAsync(string.Join(',', Csv(table), row, Csv(reader.GetName(column)), Csv(value)));
                }
                row++;
            }
        }
    }

    public async Task<string> ImportAsync(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("找不到匯入設定檔", source);
        var csvRows = ParseCsv(await File.ReadAllTextAsync(source));
        if (csvRows.Count == 0 || !csvRows[0].SequenceEqual(new[] { "Table", "Row", "Column", "Value" }))
            throw new InvalidDataException("CSV 設定檔格式不正確");
        var records = csvRows.Skip(1).Where(row => row.Count == 4)
            .GroupBy(row => (Table: row[0], Row: row[1]))
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row[2], row => row[3]));
        if (records.Keys.Any(key => !SettingsTablesInInsertOrder.Contains(key.Table)))
            throw new InvalidDataException("CSV 包含不允許的資料表");

        var backup = $"{_databasePath}.bak-before-import-{DateTime.Now:yyyyMMdd-HHmmss}";
        File.Copy(_databasePath, backup, false);
        await using var destination = new SqliteConnection(_connectionString);
        await destination.OpenAsync();
        await using var transaction = await destination.BeginTransactionAsync();
        foreach (var table in SettingsTablesInInsertOrder.Reverse())
        {
            await using var delete = destination.CreateCommand();
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = $"DELETE FROM {table}";
            await delete.ExecuteNonQueryAsync();
        }
        foreach (var table in SettingsTablesInInsertOrder)
        {
            var allowedColumns = await GetColumnsAsync(destination, table, (SqliteTransaction)transaction);
            foreach (var record in records.Where(item => item.Key.Table == table))
            {
                if (record.Value.Keys.Any(column => !allowedColumns.Contains(column)))
                    throw new InvalidDataException($"CSV 的 {table} 包含未知欄位");
                var columns = record.Value.Keys.ToArray();
                await using var insert = destination.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = $"INSERT INTO {table} ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select((_, i) => $"$p{i}"))})";
                for (var index = 0; index < columns.Length; index++) insert.Parameters.AddWithValue($"$p{index}", record.Value[columns[index]]);
                await insert.ExecuteNonQueryAsync();
            }
        }
        await transaction.CommitAsync();
        return backup;
    }

    private static readonly string[] SettingsTablesInInsertOrder =
        ["camera", "recording_setting", "event_setting", "modbus_setting", "temperature_region_setting"];

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

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string table, SqliteTransaction transaction)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        await using var reader = await command.ExecuteReaderAsync(); var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return columns;
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS recording_setting (
                id INTEGER NOT NULL PRIMARY KEY,
                enabled INTEGER NOT NULL,
                segment_seconds INTEGER NOT NULL,
                buffer_minutes INTEGER NOT NULL,
                post_event_seconds INTEGER NOT NULL,
                output_directory TEXT NOT NULL,
                reserved_space_gb INTEGER NOT NULL DEFAULT 10,
                overwrite_oldest_events INTEGER NOT NULL DEFAULT 1
            )
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS event_setting (
                camera_id TEXT NOT NULL PRIMARY KEY,
                enabled INTEGER NOT NULL DEFAULT 1,
                warning_temperature REAL NOT NULL DEFAULT 50,
                alarm_temperature REAL NOT NULL DEFAULT 70,
                trigger_seconds INTEGER NOT NULL DEFAULT 3,
                temperature_rise REAL NOT NULL DEFAULT 10,
                rise_window_seconds INTEGER NOT NULL DEFAULT 60,
                high_temperature_confirm_cycle INTEGER NOT NULL DEFAULT 3,
                high_temperature_deadband REAL NOT NULL DEFAULT 2,
                rise_confirm_cycle INTEGER NOT NULL DEFAULT 3,
                rise_deadband REAL NOT NULL DEFAULT 1,
                show_popup INTEGER NOT NULL DEFAULT 1,
                play_alarm_sound INTEGER NOT NULL DEFAULT 1,
                alarm_sound TEXT NOT NULL DEFAULT '警報音 1',
                thermal_warning_region_mask INTEGER NOT NULL DEFAULT 0,
                thermal_alarm_region_mask INTEGER NOT NULL DEFAULT 0,
                internal_warning_enabled INTEGER NOT NULL DEFAULT 1,
                internal_alarm_enabled INTEGER NOT NULL DEFAULT 1,
                internal_rise_enabled INTEGER NOT NULL DEFAULT 1,
                internal_warning_region_mask INTEGER NOT NULL DEFAULT 0,
                internal_alarm_region_mask INTEGER NOT NULL DEFAULT 0,
                internal_rise_region_mask INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (camera_id) REFERENCES camera (id) ON DELETE CASCADE
            )
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS modbus_setting (
                camera_id TEXT NOT NULL PRIMARY KEY,
                enabled INTEGER NOT NULL DEFAULT 0,
                host TEXT NOT NULL DEFAULT '127.0.0.1',
                port INTEGER NOT NULL DEFAULT 502,
                slave_id INTEGER NOT NULL DEFAULT 1,
                register_address INTEGER NOT NULL DEFAULT 0,
                alarm_register_address INTEGER NOT NULL DEFAULT 4,
                access_mode TEXT NOT NULL DEFAULT '唯讀',
                enabled_region_mask INTEGER NOT NULL DEFAULT 0,
                poll_interval_ms INTEGER NOT NULL DEFAULT 1000,
                FOREIGN KEY (camera_id) REFERENCES camera (id) ON DELETE CASCADE
            )
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS temperature_region_setting (
                camera_id TEXT NOT NULL,
                region_number INTEGER NOT NULL CHECK(region_number BETWEEN 1 AND 21),
                modbus_enabled INTEGER NOT NULL DEFAULT 0,
                internal_warning_enabled INTEGER NOT NULL DEFAULT 0,
                internal_alarm_enabled INTEGER NOT NULL DEFAULT 0,
                internal_rise_enabled INTEGER NOT NULL DEFAULT 0,
                thermal_warning_enabled INTEGER NOT NULL DEFAULT 0,
                thermal_alarm_enabled INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (camera_id, region_number),
                FOREIGN KEY (camera_id) REFERENCES camera (id) ON DELETE CASCADE
            )
            """);
        await EnsureModbusColumnAsync(db, "access_mode");
        await EnsureModbusColumnAsync(db, "alarm_register_address");
        await EnsureModbusColumnAsync(db, "enabled_region_mask");
        await EnsureEventColumnAsync(db, "temperature_rise");
        await EnsureEventColumnAsync(db, "rise_window_seconds");
        await EnsureEventColumnAsync(db, "high_temperature_confirm_cycle");
        await EnsureEventColumnAsync(db, "high_temperature_deadband");
        await EnsureEventColumnAsync(db, "rise_confirm_cycle");
        await EnsureEventColumnAsync(db, "rise_deadband");
        await EnsureEventColumnAsync(db, "show_popup");
        await EnsureEventColumnAsync(db, "play_alarm_sound");
        await EnsureEventColumnAsync(db, "thermal_warning_region_mask");
        await EnsureEventColumnAsync(db, "thermal_alarm_region_mask");
        await EnsureEventColumnAsync(db, "internal_warning_enabled");
        await EnsureEventColumnAsync(db, "internal_alarm_enabled");
        await EnsureEventColumnAsync(db, "internal_rise_enabled");
        await EnsureEventColumnAsync(db, "internal_warning_region_mask");
        await EnsureEventColumnAsync(db, "internal_alarm_region_mask");
        await EnsureEventColumnAsync(db, "internal_rise_region_mask");
        await EnsureEventColumnAsync(db, "alarm_sound");
        await EnsureRecordingColumnAsync(db, "reserved_space_gb");
        await EnsureRecordingColumnAsync(db, "overwrite_oldest_events");
        await EnsureCameraColumnAsync(db, "source_type");
        await EnsureCameraColumnAsync(db, "imaging_type");
        await EnsureCameraColumnAsync(db, "stream_type");
    }

    public async Task<List<Camera>> LoadCamerasAsync()
    {
        await using var db = CreateContext();
        return await db.Cameras
            .AsNoTracking()
            .OrderBy(camera => camera.SortOrder)
            .Select(camera => new Camera
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

    public async Task AddCameraAsync(Camera camera)
    {
        await using var db = CreateContext();
        db.Cameras.Add(new CameraEntity
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

    public async Task UpdateCameraAsync(Camera camera)
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
            entity = new RecordingSettingEntity { Id = 1 };
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
                setting.InternalWarningEnabled, setting.InternalAlarmEnabled, setting.InternalRiseEnabled,
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
                entity = new EventSettingEntity { CameraId = setting.CameraId };
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
            entity.InternalWarningEnabled = setting.InternalWarningEnabled;
            entity.InternalAlarmEnabled = setting.InternalAlarmEnabled;
            entity.InternalRiseEnabled = setting.InternalRiseEnabled;
            entity.InternalWarningRegionMask = setting.InternalWarningRegionMask;
            entity.InternalAlarmRegionMask = setting.InternalAlarmRegionMask;
            entity.InternalRiseRegionMask = setting.InternalRiseRegionMask;
            for (var number = 1; number <= 21; number++)
            {
                if (!regions.TryGetValue((setting.CameraId, number), out var region))
                {
                    region = new TemperatureRegionSettingEntity { CameraId = setting.CameraId, RegionNumber = number };
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

    public async Task<List<ModbusSettings>> LoadModbusSettingsAsync()
    {
        await using var db = CreateContext();
        return await db.ModbusSettings.AsNoTracking()
            .Select(setting => new ModbusSettings(setting.CameraId, setting.Enabled, setting.Host,
                setting.Port, setting.SlaveId, setting.RegisterAddress, setting.AlarmRegisterAddress,
                setting.AccessMode, setting.EnabledRegionMask, setting.PollIntervalMs))
            .ToListAsync();
    }

    public async Task SaveModbusSettingsAsync(IReadOnlyCollection<ModbusSettings> settings)
    {
        await using var db = CreateContext();
        var existing = await db.ModbusSettings.ToDictionaryAsync(item => item.CameraId,
            StringComparer.OrdinalIgnoreCase);
        var regions = await db.TemperatureRegionSettings
            .ToDictionaryAsync(item => (item.CameraId, item.RegionNumber));
        foreach (var setting in settings)
        {
            if (!existing.TryGetValue(setting.CameraId, out var entity))
            {
                entity = new ModbusSettingEntity { CameraId = setting.CameraId };
                db.ModbusSettings.Add(entity);
            }
            entity.Enabled = setting.Enabled;
            entity.Host = setting.Host;
            entity.Port = setting.Port;
            entity.SlaveId = 1;
            entity.RegisterAddress = setting.RegisterAddress;
            entity.AlarmRegisterAddress = setting.AlarmRegisterAddress;
            entity.AccessMode = setting.AccessMode;
            entity.EnabledRegionMask = setting.EnabledRegionMask;
            for (var number = 1; number <= 21; number++)
            {
                if (!regions.TryGetValue((setting.CameraId, number), out var region))
                {
                    region = new TemperatureRegionSettingEntity { CameraId = setting.CameraId, RegionNumber = number };
                    db.TemperatureRegionSettings.Add(region);
                    regions[(setting.CameraId, number)] = region;
                }
                region.ModbusEnabled = (setting.EnabledRegionMask & (1 << (number - 1))) != 0;
            }
            entity.PollIntervalMs = setting.PollIntervalMs;
        }
        await db.SaveChangesAsync();
    }

    public async Task SaveOrderAsync(IReadOnlyList<Camera> cameras)
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

    private static async Task EnsureCameraColumnAsync(ThermalGuardDbContext db, string columnName)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('camera') WHERE name = {columnName}")
            .SingleAsync();
        if (count == 0)
        {
            var sql = columnName switch
            {
                "source_type" => "ALTER TABLE camera ADD COLUMN source_type TEXT NOT NULL DEFAULT 'RTSP 串流'",
                "imaging_type" => "ALTER TABLE camera ADD COLUMN imaging_type TEXT NOT NULL DEFAULT '可見光'",
                "stream_type" => "ALTER TABLE camera ADD COLUMN stream_type TEXT NOT NULL DEFAULT '主碼流'",
                _ => throw new ArgumentOutOfRangeException(nameof(columnName))
            };
            await db.Database.ExecuteSqlRawAsync(sql);
        }
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

    private static async Task EnsureRecordingColumnAsync(ThermalGuardDbContext db, string columnName)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('recording_setting') WHERE name = {columnName}")
            .SingleAsync();
        if (count != 0) return;
        var sql = columnName switch
        {
            "reserved_space_gb" => "ALTER TABLE recording_setting ADD COLUMN reserved_space_gb INTEGER NOT NULL DEFAULT 10",
            "overwrite_oldest_events" => "ALTER TABLE recording_setting ADD COLUMN overwrite_oldest_events INTEGER NOT NULL DEFAULT 1",
            _ => throw new ArgumentOutOfRangeException(nameof(columnName))
        };
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task EnsureEventColumnAsync(ThermalGuardDbContext db, string columnName)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('event_setting') WHERE name = {columnName}")
            .SingleAsync();
        if (count != 0) return;
        var sql = columnName switch
        {
            "temperature_rise" => "ALTER TABLE event_setting ADD COLUMN temperature_rise REAL NOT NULL DEFAULT 10",
            "rise_window_seconds" => "ALTER TABLE event_setting ADD COLUMN rise_window_seconds INTEGER NOT NULL DEFAULT 60",
            "high_temperature_confirm_cycle" => "ALTER TABLE event_setting ADD COLUMN high_temperature_confirm_cycle INTEGER NOT NULL DEFAULT 3",
            "high_temperature_deadband" => "ALTER TABLE event_setting ADD COLUMN high_temperature_deadband REAL NOT NULL DEFAULT 2",
            "rise_confirm_cycle" => "ALTER TABLE event_setting ADD COLUMN rise_confirm_cycle INTEGER NOT NULL DEFAULT 3",
            "rise_deadband" => "ALTER TABLE event_setting ADD COLUMN rise_deadband REAL NOT NULL DEFAULT 1",
            "show_popup" => "ALTER TABLE event_setting ADD COLUMN show_popup INTEGER NOT NULL DEFAULT 1",
            "play_alarm_sound" => "ALTER TABLE event_setting ADD COLUMN play_alarm_sound INTEGER NOT NULL DEFAULT 1",
            "thermal_warning_region_mask" => "ALTER TABLE event_setting ADD COLUMN thermal_warning_region_mask INTEGER NOT NULL DEFAULT 0",
            "thermal_alarm_region_mask" => "ALTER TABLE event_setting ADD COLUMN thermal_alarm_region_mask INTEGER NOT NULL DEFAULT 0",
            "internal_warning_enabled" => "ALTER TABLE event_setting ADD COLUMN internal_warning_enabled INTEGER NOT NULL DEFAULT 1",
            "internal_alarm_enabled" => "ALTER TABLE event_setting ADD COLUMN internal_alarm_enabled INTEGER NOT NULL DEFAULT 1",
            "internal_rise_enabled" => "ALTER TABLE event_setting ADD COLUMN internal_rise_enabled INTEGER NOT NULL DEFAULT 1",
            "internal_warning_region_mask" => "ALTER TABLE event_setting ADD COLUMN internal_warning_region_mask INTEGER NOT NULL DEFAULT 0",
            "internal_alarm_region_mask" => "ALTER TABLE event_setting ADD COLUMN internal_alarm_region_mask INTEGER NOT NULL DEFAULT 0",
            "internal_rise_region_mask" => "ALTER TABLE event_setting ADD COLUMN internal_rise_region_mask INTEGER NOT NULL DEFAULT 0",
            "alarm_sound" => "ALTER TABLE event_setting ADD COLUMN alarm_sound TEXT NOT NULL DEFAULT '警報音 1'",
            _ => throw new ArgumentOutOfRangeException(nameof(columnName))
        };
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task EnsureModbusColumnAsync(ThermalGuardDbContext db, string columnName)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS Value FROM pragma_table_info('modbus_setting') WHERE name = {columnName}")
            .SingleAsync();
        if (count != 0) return;
        var sql = columnName switch
        {
            "access_mode" => "ALTER TABLE modbus_setting ADD COLUMN access_mode TEXT NOT NULL DEFAULT '唯讀'",
            "alarm_register_address" => "ALTER TABLE modbus_setting ADD COLUMN alarm_register_address INTEGER NOT NULL DEFAULT 4",
            "enabled_region_mask" => "ALTER TABLE modbus_setting ADD COLUMN enabled_region_mask INTEGER NOT NULL DEFAULT 0",
            _ => throw new ArgumentOutOfRangeException(nameof(columnName))
        };
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
