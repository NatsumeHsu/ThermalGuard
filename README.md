# ThermalGuard Pro

專案已改為 **C# / WPF / LibVLCSharp** 架構，支援 RTSP 多畫面、SQLite 攝影機設定、拖放排序與 Modbus TCP 溫度/狀態輪詢。

## 開發執行

```powershell
dotnet restore ThermalGuard.slnx
dotnet run --project ThermalGuard/ThermalGuard.csproj
```

資料庫位於程式輸出目錄的 `data/nvr.db`。若要沿用舊版資料，將既有 `nvr.db` 放到該目錄即可；資料表 schema 維持 `camera` 與 `modbus_setting` 不變。LibVLC native runtime 由 `VideoLAN.LibVLC.Windows` NuGet 套件一併部署，不再需要 `ffmpeg.exe`。

## 發佈

```powershell
dotnet publish ThermalGuard/ThermalGuard.csproj -c Release -r win-x64 --self-contained true
```
