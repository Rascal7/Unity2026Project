using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows 64-bit 릴리스 배포판 빌드 및 Inno Setup 설치 마법사 생성을 돕는 에디터 헬퍼.
/// </summary>
public static class BuildScript
{
    [MenuItem("Tools/Build/★ Build All (Game + Installer) ★", priority = 0)]
    public static void BuildAll()
    {
        Debug.Log("<color=cyan>[BuildScript]</color> === 전체 빌드 시작 (게임 빌드 → 설치 마법사 생성) ===");
        bool success = BuildWindowsRelease(false);
        if (success)
        {
            CompileInstaller();
        }
        else
        {
            Debug.LogError("<color=red>[BuildScript] 게임 빌드에 실패하여 설치 마법사 생성을 중단합니다.</color>");
        }
    }

    [MenuItem("Tools/Build/1. Build Windows 64-bit (Game Only)", priority = 10)]
    public static void MenuBuildWindowsRelease()
    {
        BuildWindowsRelease(true);
    }

    public static bool BuildWindowsRelease(bool revealInFinder = true)
    {
        string projectDir = Directory.GetCurrentDirectory();
        string buildDir = Path.Combine(projectDir, "Builds", "Windows");
        if (!Directory.Exists(buildDir))
        {
            Directory.CreateDirectory(buildDir);
        }

        string exePath = Path.Combine(buildDir, "Unity2026Project.exe");

        // Build Settings에 등록되고 활성화된 씬 목록 수집 (Index 0: Lobby)
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildScript] Build Settings에 등록된 활성 씬이 없습니다!");
            return false;
        }

        // 프로젝트 버전 설정
        PlayerSettings.bundleVersion = "beta1.0";

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None // 릴리스 배포판 (Development Build 해제)
        };

        Debug.Log($"<color=cyan>[BuildScript]</color> 배포판 빌드 시작: {exePath}");
        Debug.Log($"<color=cyan>[BuildScript]</color> 포함된 씬 ({scenes.Length}개): {string.Join(", ", scenes)}");

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"<color=lime>★ [BuildScript] 게임 빌드 성공! ★</color>");
            Debug.Log($"출력 위치: {exePath}");
            Debug.Log($"소요 시간: {summary.totalTime:c}, 총 크기: {summary.totalSize / (1024 * 1024)} MB");

            if (revealInFinder && !Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(exePath);
            }
            return true;
        }
        else
        {
            Debug.LogError($"<color=red>✖ [BuildScript] 게임 빌드 실패! ✖</color> 총 에러 수: {summary.totalErrors}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
            return false;
        }
    }

    [MenuItem("Tools/Build/2. Compile Inno Setup (Create Setup.exe)", priority = 20)]
    public static void CompileInstaller()
    {
        string isccPath = FindIsccPath();
        if (string.IsNullOrEmpty(isccPath))
        {
            Debug.LogError("[BuildScript] Inno Setup 컴파일러(ISCC.exe)를 찾을 수 없습니다. Inno Setup을 먼저 설치해 주세요 (https://jrsoftware.org/isdl.php).");
            EditorUtility.DisplayDialog("Inno Setup 필요", "Inno Setup 컴파일러(ISCC.exe)가 설치되어 있지 않습니다.\n\nInno Setup 6 또는 7을 설치한 후 다시 시도해 주세요.", "확인");
            return;
        }

        string issPath = Path.Combine(Directory.GetCurrentDirectory(), "Installer", "Unity2026_Setup.iss");
        if (!File.Exists(issPath))
        {
            Debug.LogError($"[BuildScript] 설치 스크립트 파일을 찾을 수 없습니다: {issPath}");
            return;
        }

        string exeGame = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Windows", "Unity2026Project.exe");
        if (!File.Exists(exeGame))
        {
            Debug.LogError($"[BuildScript] Builds/Windows/Unity2026Project.exe 가 없습니다. 먼저 [1. Build Windows 64-bit]를 실행해 게임을 빌드해 주세요.");
            EditorUtility.DisplayDialog("게임 빌드 필요", "Builds/Windows 폴더에 게임 빌드가 없습니다.\n\n먼저 [1. Build Windows 64-bit]를 실행해 주세요.", "확인");
            return;
        }

        var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = isccPath;
        process.StartInfo.Arguments = $"\"{issPath}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        Debug.Log($"<color=cyan>[BuildScript]</color> Inno Setup 컴파일러 실행 중... ({issPath})");
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "Installer");
            var installerFiles = Directory.Exists(outDir) ? new DirectoryInfo(outDir).GetFiles("*.exe") : new FileInfo[0];
            var setupExe = installerFiles.OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            string setupPath = setupExe != null ? setupExe.FullName : Path.Combine(outDir, "Unity2026_Setup_beta1.0.exe");

            Debug.Log($"<color=lime>★ [BuildScript] 설치 마법사(Setup.exe) 생성 완료! ★</color>\n출력 파일: {setupPath}");
            if (File.Exists(setupPath))
            {
                EditorUtility.RevealInFinder(setupPath);
            }
        }
        else
        {
            Debug.LogError($"<color=red>[BuildScript] Inno Setup 컴파일 실패:</color>\n{output}");
        }
    }

    private static string FindIsccPath()
    {
        string[] candidates = new[]
        {
            @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            @"C:\Program Files\Inno Setup 6\ISCC.exe",
            @"C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
            @"C:\Program Files\Inno Setup 7\ISCC.exe"
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // PATH 환경 변수 확인
        string envPath = System.Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            foreach (var dir in envPath.Split(';'))
            {
                string combined = Path.Combine(dir.Trim(), "ISCC.exe");
                if (File.Exists(combined)) return combined;
            }
        }

        return null;
    }
}
