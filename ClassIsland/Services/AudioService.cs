using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace ClassIsland.Services;

public class AudioService(ILogger<AudioService> logger) : IAudioService
{
    /// <summary>
    /// 音频引擎惰性初始化。部分环境（如缺少 libminiaudio、无声卡、缺少系统音频库）会导致初始化失败，
    /// 此时音频功能降级为不可用，但不能让整个应用启动崩溃。
    /// </summary>
    private readonly Lazy<AudioEngine?> _audioEngine = new(() =>
    {
        try
        {
            return Task.Run(() => (AudioEngine?)new MiniAudioEngine()).Result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "初始化音频引擎失败，音频功能将不可用");
            return null;
        }
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    private ILogger<AudioService> Logger { get; } = logger;

    private RefCounted<AudioPlaybackDevice>? _sharedAudioPlaybackDevice;

    private object _audioPlaybackDeviceInitializeLock = new();

    private AudioEngine? EngineOrNull => _audioEngine.Value;

    public AudioEngine AudioEngine
    {
        get
        {
            if (OperatingSystem.IsWindows() && Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            {
                throw new InvalidOperationException(
                    "出于线程安全考虑，禁止在非 MTA 线程上调用 AudioEngine。请在 MTA 线程上调用 AudioEngine。详细请见 https://github.com/ClassIsland/ClassIsland/issues/1333#issuecomment-3505591836");
            }
            return _audioEngine.Value ?? throw new InvalidOperationException("音频引擎不可用，当前环境无法初始化音频后端。");
        }
    }

    public AudioPlaybackDevice? TryInitializeDefaultPlaybackDevice() =>
        TryInitializeDefaultPlaybackDeviceAsync().Result;

    public Task<AudioPlaybackDevice?> TryInitializeDefaultPlaybackDeviceAsync() => 
        Task.Run(TryInitializeDefaultPlaybackDeviceInternal);

    public Task<RefCounted<AudioPlaybackDevice>.Lease?> TryInitializeDefaultPlaybackDeviceSafeAsync() => Task.Run(() =>
    {
        lock (_audioPlaybackDeviceInitializeLock)
        {
            if (_sharedAudioPlaybackDevice?.IsValueDisposed == false)
            {
                var lease = _sharedAudioPlaybackDevice.Rent();
                Logger.LogDebug("使用了缓存的音频设备 {} (Id={})", lease.Value.Info?.Name, lease.Value.Info?.Id);
                return lease;
            }

            if (TryInitializeDefaultPlaybackDeviceInternal() is not { } device)
            {
                return null;
            }
            _sharedAudioPlaybackDevice = new RefCounted<AudioPlaybackDevice>(device);
            var lease2 = _sharedAudioPlaybackDevice.Rent();
            _sharedAudioPlaybackDevice.Dispose();
            return lease2;
        }
    });

    private AudioPlaybackDevice? TryInitializeDefaultPlaybackDeviceInternal()
    {
        try
        {
            if (EngineOrNull is not { } engine)
            {
                return null;
            }

            var deviceInfo = engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
            if (deviceInfo == default)
            {
                Logger.LogDebug("找不到可用的音频设备");
                return null;
            }

            Logger.LogDebug("初始化音频设备 {} (Id={})", deviceInfo.Name, deviceInfo.Id);
            var device = engine.InitializePlaybackDevice(deviceInfo, IAudioService.DefaultAudioFormat);
            device.MasterMixer.Volume = 1.0f;
            device.Start();
            return device;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "初始化音频设备失败");
            return null;
        }
        
    }

    public Task PlayAudioAsync(Stream audio, float volume, CancellationToken? cancellationToken = null) => Task.Run(async () =>
    {
        using var audioStream = audio;
        cancellationToken ??= CancellationToken.None;
        using var lease = await TryInitializeDefaultPlaybackDeviceSafeAsync();
        if (lease == null)
        {
            return;
        }

        var device = lease.Value;
        if (EngineOrNull is not { } engine)
        {
            return;
        }

        using var player = new SoundPlayer(engine, IAudioService.DefaultAudioFormat,
            new StreamDataProvider(engine, IAudioService.DefaultAudioFormat, audio));
        player.Volume = volume;
        Logger.LogDebug("开始播放音频 {}", audio.GetHashCode());
        device.MasterMixer.AddComponent(player);
        var tcs = new TaskCompletionSource<bool>();

        player.PlaybackEnded += OnPlayerOnPlaybackEnded;
        cancellationToken.Value.Register(() =>
        {
            Logger.LogDebug("取消播放音频 {}", audio.GetHashCode());
            tcs.TrySetResult(false);
        });
        player.Play();
        tcs.Task.Wait();  // 不要在此处 await，否则会导致设备停止过程阻塞，无法完成播放流程。
        Logger.LogDebug("结束播放音频 {}", audio.GetHashCode());
        player.PlaybackEnded -= OnPlayerOnPlaybackEnded;

        return;

        void OnPlayerOnPlaybackEnded(object? sender, EventArgs args)
        {
            tcs.TrySetResult(true);
        }
    });

    public async Task PlayAudioAsync(string filePath, float volume, CancellationToken? cancellationToken = null)
    {
        using var audio = File.OpenRead(filePath);
        await PlayAudioAsync(audio, volume, cancellationToken);
    }

    public void Dispose()
    {
        if (_audioEngine.IsValueCreated)
        {
            _audioEngine.Value?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
