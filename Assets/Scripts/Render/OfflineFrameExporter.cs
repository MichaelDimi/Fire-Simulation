using System;
using System.IO;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Camera))]
public class OfflineFrameExporter : MonoBehaviour
{
    [Header("Trigger")]
    public bool autoStartOnPlay = false;
    public KeyCode toggleCaptureKey = KeyCode.F9;

    [Header("Clip")]
    [Min(1)] public int captureFrameRate = 30;
    [Min(1)] public int captureDurationSeconds = 4;
    [Min(1)] public int screenshotSuperSize = 1;
    public string outputFolderName = "Captures";
    public string clipName = "fire_demo";

    [Header("Encoding")]
    public bool encodeMp4AfterCapture = true;
    public string ffmpegExecutable = "ffmpeg";
    [Range(0, 51)] public int mp4Crf = 18;
    public string mp4Preset = "medium";
    public bool keepPngFramesAfterEncoding = true;

    [Header("Simulation Quality During Capture")]
    public SmokeSimulation simulation;
    public bool overrideSimulationQuality = true;
    [Range(1, 16)] public int captureMaxSimulationSubsteps = 10;
    [Range(1, 128)] public int capturePressureIterations = 40;

    bool _isCapturing;
    bool _isEncoding;
    bool _cancelRequested;
    Coroutine _captureRoutine;
    Coroutine _encodeRoutine;
    Process _encodingProcess;
    bool _warnedUnsupportedInputSystemKey;

    int _originalTargetFrameRate;
    float _originalCaptureDeltaTime;
    int _originalMaxSimulationSubsteps;
    int _originalPressureIterations;
    bool _storedSimulationOverrides;

    public bool IsCapturing => _isCapturing;
    public bool IsEncoding => _isEncoding;

    void Start()
    {
        ResolveSimulation();

        if (autoStartOnPlay && Application.isPlaying)
            BeginCapture();
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;

        if (IsToggleCapturePressedThisFrame())
        {
            if (_isCapturing)
                RequestStopCapture();
            else
                BeginCapture();
        }
    }

    void OnDisable()
    {
        if (!_isCapturing)
        {
            StopEncodingProcessIfRunning();
            return;
        }

        _cancelRequested = true;
        StopEncodingProcessIfRunning();
    }

    [ContextMenu("Begin Offline Capture")]
    public void BeginCapture()
    {
        if (!Application.isPlaying || _isCapturing || _isEncoding)
            return;

        _captureRoutine = StartCoroutine(CaptureSequence());
    }

    [ContextMenu("Stop Offline Capture")]
    public void RequestStopCapture()
    {
        if (!_isCapturing)
            return;

        _cancelRequested = true;
    }

    SmokeSimulation ResolveSimulation()
    {
        if (simulation != null)
            return simulation;

        simulation = FindObjectOfType<SmokeSimulation>();
        return simulation;
    }

    IEnumerator CaptureSequence()
    {
        _isCapturing = true;
        _cancelRequested = false;

        string sessionFolder = PrepareOutputFolder();
        int totalFrames = Mathf.Max(1, captureFrameRate * captureDurationSeconds);

        _originalTargetFrameRate = Application.targetFrameRate;
        _originalCaptureDeltaTime = Time.captureDeltaTime;

        ApplySimulationOverrides();

        Application.targetFrameRate = captureFrameRate;
        Time.captureDeltaTime = 1f / captureFrameRate;

        UnityEngine.Debug.Log(
            $"Offline capture started: {totalFrames} frames @ {captureFrameRate} FPS -> {sessionFolder}"
        );

        int frameIndex = 0;
        while (frameIndex < totalFrames && !_cancelRequested)
        {
            yield return new WaitForEndOfFrame();

            string framePath = Path.Combine(sessionFolder, $"frame_{frameIndex:D05}.png");
            ScreenCapture.CaptureScreenshot(framePath, screenshotSuperSize);
            frameIndex++;

            if (frameIndex % Mathf.Max(1, captureFrameRate) == 0)
                UnityEngine.Debug.Log($"Offline capture progress: {frameIndex}/{totalFrames} frames");
        }

        Time.captureDeltaTime = _originalCaptureDeltaTime;
        Application.targetFrameRate = _originalTargetFrameRate;
        RestoreSimulationOverrides();

        string status = _cancelRequested ? "cancelled" : "finished";
        UnityEngine.Debug.Log($"Offline capture {status}. Frames written: {frameIndex}. Folder: {sessionFolder}");

        if (!_cancelRequested && frameIndex > 0 && encodeMp4AfterCapture)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            if (_encodeRoutine != null)
                StopCoroutine(_encodeRoutine);

            _encodeRoutine = StartCoroutine(EncodeMp4Routine(sessionFolder, frameIndex));
        }

        _captureRoutine = null;
        _isCapturing = false;
    }

    string PrepareOutputFolder()
    {
        string safeClipName = string.IsNullOrWhiteSpace(clipName) ? "fire_demo" : clipName.Trim();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string root = Path.Combine(projectRoot, outputFolderName);
        string session = Path.Combine(root, $"{safeClipName}_{timestamp}");

        Directory.CreateDirectory(session);
        return session;
    }

    void ApplySimulationOverrides()
    {
        _storedSimulationOverrides = false;

        if (!overrideSimulationQuality)
            return;

        SmokeSimulation sim = ResolveSimulation();
        if (sim == null)
            return;

        _originalMaxSimulationSubsteps = sim.maxSimulationSubsteps;
        _originalPressureIterations = sim.pressureIterations;
        _storedSimulationOverrides = true;

        sim.maxSimulationSubsteps = Mathf.Max(1, captureMaxSimulationSubsteps);
        sim.pressureIterations = Mathf.Max(1, capturePressureIterations);
    }

    void RestoreSimulationOverrides()
    {
        if (!_storedSimulationOverrides)
            return;

        SmokeSimulation sim = ResolveSimulation();
        if (sim != null)
        {
            sim.maxSimulationSubsteps = _originalMaxSimulationSubsteps;
            sim.pressureIterations = _originalPressureIterations;
        }

        _storedSimulationOverrides = false;
    }

    IEnumerator EncodeMp4Routine(string sessionFolder, int frameCount)
    {
        _isEncoding = true;

        string safeClipName = string.IsNullOrWhiteSpace(clipName) ? "fire_demo" : clipName.Trim();
        string outputMp4Path = Path.Combine(sessionFolder, $"{safeClipName}.mp4");
        string resolvedFfmpegExecutable = ResolveFfmpegExecutable();

        if (string.IsNullOrWhiteSpace(resolvedFfmpegExecutable))
        {
            UnityEngine.Debug.LogWarning(
                "MP4 encode skipped: ffmpeg executable could not be resolved. " +
                "Set an absolute path in OfflineFrameExporter.ffmpegExecutable."
            );
            _isEncoding = false;
            _encodeRoutine = null;
            yield break;
        }

        string inputPattern = "frame_%05d.png";
        string args =
            $"-y -framerate {captureFrameRate} -i \"{inputPattern}\" -vframes {frameCount} " +
            "-vf \"pad=ceil(iw/2)*2:ceil(ih/2)*2\" " +
            $"-c:v libx264 -crf {Mathf.Clamp(mp4Crf, 0, 51)} -preset {mp4Preset} -pix_fmt yuv420p \"{outputMp4Path}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedFfmpegExecutable,
            Arguments = args,
            WorkingDirectory = sessionFolder,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning(
                "MP4 encode skipped. ffmpeg may not be installed or reachable.\n" + ex.Message
            );
            _encodingProcess = null;
            _isEncoding = false;
            _encodeRoutine = null;
            yield break;
        }

        if (process == null)
        {
            UnityEngine.Debug.LogWarning("MP4 encode skipped: failed to start ffmpeg process.");
            _encodingProcess = null;
            _isEncoding = false;
            _encodeRoutine = null;
            yield break;
        }

        _encodingProcess = process;
        UnityEngine.Debug.Log($"MP4 encoding started: {outputMp4Path}");

        while (!process.HasExited)
            yield return null;

        bool encodeSucceeded = process.ExitCode == 0 && File.Exists(outputMp4Path);
        process.Dispose();

        _encodingProcess = null;
        _isEncoding = false;
        _encodeRoutine = null;

        if (!encodeSucceeded)
        {
            UnityEngine.Debug.LogWarning(
                "MP4 encode failed. Keep PNG frames and check ffmpeg arguments/path. " +
                $"Executable: {startInfo.FileName}"
            );
            yield break;
        }

        UnityEngine.Debug.Log($"MP4 export complete: {outputMp4Path}");

        if (!keepPngFramesAfterEncoding)
            DeleteFrameSequence(sessionFolder);
    }

    static void DeleteFrameSequence(string folderPath)
    {
        string[] frames = Directory.GetFiles(folderPath, "frame_*.png");
        for (int i = 0; i < frames.Length; i++)
            File.Delete(frames[i]);
    }

    string ResolveFfmpegExecutable()
    {
        string configured = string.IsNullOrWhiteSpace(ffmpegExecutable) ? "ffmpeg" : ffmpegExecutable.Trim();

        if (Path.IsPathRooted(configured))
            return File.Exists(configured) ? configured : null;

        string homebrewArmPath = "/opt/homebrew/bin/ffmpeg";
        if (File.Exists(homebrewArmPath))
            return homebrewArmPath;

        string homebrewIntelPath = "/usr/local/bin/ffmpeg";
        if (File.Exists(homebrewIntelPath))
            return homebrewIntelPath;

        return configured;
    }

    void StopEncodingProcessIfRunning()
    {
        if (_encodingProcess == null)
            return;

        Process process = _encodingProcess;
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
            // Best effort cleanup only.
        }
        finally
        {
            process.Dispose();
            _encodingProcess = null;
            _isEncoding = false;
        }
    }

    bool IsToggleCapturePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (IsPressedWithInputSystem())
            return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return UnityEngine.Input.GetKeyDown(toggleCaptureKey);
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    bool IsPressedWithInputSystem()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        switch (toggleCaptureKey)
        {
            case KeyCode.F1: return keyboard.f1Key.wasPressedThisFrame;
            case KeyCode.F2: return keyboard.f2Key.wasPressedThisFrame;
            case KeyCode.F3: return keyboard.f3Key.wasPressedThisFrame;
            case KeyCode.F4: return keyboard.f4Key.wasPressedThisFrame;
            case KeyCode.F5: return keyboard.f5Key.wasPressedThisFrame;
            case KeyCode.F6: return keyboard.f6Key.wasPressedThisFrame;
            case KeyCode.F7: return keyboard.f7Key.wasPressedThisFrame;
            case KeyCode.F8: return keyboard.f8Key.wasPressedThisFrame;
            case KeyCode.F9: return keyboard.f9Key.wasPressedThisFrame;
            case KeyCode.F10: return keyboard.f10Key.wasPressedThisFrame;
            case KeyCode.F11: return keyboard.f11Key.wasPressedThisFrame;
            case KeyCode.F12: return keyboard.f12Key.wasPressedThisFrame;
            case KeyCode.Space: return keyboard.spaceKey.wasPressedThisFrame;
            case KeyCode.Escape: return keyboard.escapeKey.wasPressedThisFrame;
            case KeyCode.Return: return keyboard.enterKey.wasPressedThisFrame;
            case KeyCode.KeypadEnter: return keyboard.numpadEnterKey.wasPressedThisFrame;
            case KeyCode.A: return keyboard.aKey.wasPressedThisFrame;
            case KeyCode.B: return keyboard.bKey.wasPressedThisFrame;
            case KeyCode.C: return keyboard.cKey.wasPressedThisFrame;
            case KeyCode.D: return keyboard.dKey.wasPressedThisFrame;
            case KeyCode.E: return keyboard.eKey.wasPressedThisFrame;
            case KeyCode.F: return keyboard.fKey.wasPressedThisFrame;
            case KeyCode.G: return keyboard.gKey.wasPressedThisFrame;
            case KeyCode.H: return keyboard.hKey.wasPressedThisFrame;
            case KeyCode.I: return keyboard.iKey.wasPressedThisFrame;
            case KeyCode.J: return keyboard.jKey.wasPressedThisFrame;
            case KeyCode.K: return keyboard.kKey.wasPressedThisFrame;
            case KeyCode.L: return keyboard.lKey.wasPressedThisFrame;
            case KeyCode.M: return keyboard.mKey.wasPressedThisFrame;
            case KeyCode.N: return keyboard.nKey.wasPressedThisFrame;
            case KeyCode.O: return keyboard.oKey.wasPressedThisFrame;
            case KeyCode.P: return keyboard.pKey.wasPressedThisFrame;
            case KeyCode.Q: return keyboard.qKey.wasPressedThisFrame;
            case KeyCode.R: return keyboard.rKey.wasPressedThisFrame;
            case KeyCode.S: return keyboard.sKey.wasPressedThisFrame;
            case KeyCode.T: return keyboard.tKey.wasPressedThisFrame;
            case KeyCode.U: return keyboard.uKey.wasPressedThisFrame;
            case KeyCode.V: return keyboard.vKey.wasPressedThisFrame;
            case KeyCode.W: return keyboard.wKey.wasPressedThisFrame;
            case KeyCode.X: return keyboard.xKey.wasPressedThisFrame;
            case KeyCode.Y: return keyboard.yKey.wasPressedThisFrame;
            case KeyCode.Z: return keyboard.zKey.wasPressedThisFrame;
            case KeyCode.Alpha0: return keyboard.digit0Key.wasPressedThisFrame;
            case KeyCode.Alpha1: return keyboard.digit1Key.wasPressedThisFrame;
            case KeyCode.Alpha2: return keyboard.digit2Key.wasPressedThisFrame;
            case KeyCode.Alpha3: return keyboard.digit3Key.wasPressedThisFrame;
            case KeyCode.Alpha4: return keyboard.digit4Key.wasPressedThisFrame;
            case KeyCode.Alpha5: return keyboard.digit5Key.wasPressedThisFrame;
            case KeyCode.Alpha6: return keyboard.digit6Key.wasPressedThisFrame;
            case KeyCode.Alpha7: return keyboard.digit7Key.wasPressedThisFrame;
            case KeyCode.Alpha8: return keyboard.digit8Key.wasPressedThisFrame;
            case KeyCode.Alpha9: return keyboard.digit9Key.wasPressedThisFrame;
            default:
                if (!_warnedUnsupportedInputSystemKey)
                {
                    UnityEngine.Debug.LogWarning(
                        $"OfflineFrameExporter: '{toggleCaptureKey}' is not mapped for Input System hotkeys yet. " +
                        "Use F1-F12, A-Z, 0-9, Space, Escape, Return, or KeypadEnter."
                    );
                    _warnedUnsupportedInputSystemKey = true;
                }
                return false;
        }
    }
#endif
}
