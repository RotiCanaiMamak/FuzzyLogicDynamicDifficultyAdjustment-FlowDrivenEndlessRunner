using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Profiling;

public class PerformanceTracker : MonoBehaviour
{
    public float interval = 1.0f; 
    public float duration = 300f; 
    private string filePath;

    void Start()
    {
        filePath = Path.Combine(Application.persistentDataPath, "PerformanceLog.csv");
        File.WriteAllText(filePath, "Time(s), FPS, CPU(ms), Memory(MB)\n");
        StartCoroutine(RecordMetrics());
    }

    IEnumerator RecordMetrics()
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            //calculate FPS
            float fps = 1.0f / Time.unscaledDeltaTime;

            //capture Memory (Total Allocated in MB)
            long memory = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);

            //capture CPU Frame Time (ms)
            float cpuTime = Time.unscaledDeltaTime * 1000f;

            //save log to CSV
            string logEntry = $"{elapsed:F1}, {fps:F2}, {cpuTime:F2}, {memory}\n";
            File.AppendAllText(filePath, logEntry);

            yield return new WaitForSecondsRealtime(interval);
            elapsed += interval;
        }

        Debug.Log($"Logging complete. File saved to: {filePath}");
    }
}