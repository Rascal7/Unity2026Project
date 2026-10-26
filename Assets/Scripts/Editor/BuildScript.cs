using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows 64-bit 릴리스 배포판 빌드를 수행하는 에디터 헬퍼.
/// Unity 상단 메뉴 [Tools > Build > Build Windows 64-bit (Release)] 또는
/// 커맨드라인(-executeMethod BuildScript.BuildWindowsRelease)에서 호출할 수 있습니다.
/// </summary>
public static class BuildScript
{
    [MenuItem("Tools/Build/Build Windows 64-bit (Release)")]
    public static void BuildWindowsRelease()
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
            return;
        }

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
            Debug.Log($"<color=lime>★ [BuildScript] 빌드 성공! ★</color>");
            Debug.Log($"출력 위치: {exePath}");
            Debug.Log($"소요 시간: {summary.totalTime:c}, 총 크기: {summary.totalSize / (1024 * 1024)} MB");

            // 에디터 환경이면 파일 탐색기 열기
            if (!Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(exePath);
            }
        }
        else if (summary.result == BuildResult.Failed)
        {
            Debug.LogError($"<color=red>✖ [BuildScript] 빌드 실패! ✖</color> 총 에러 수: {summary.totalErrors}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
