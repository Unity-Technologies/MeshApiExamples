using UnityEngine;

public class PerformanceIndicator : MonoBehaviour
{
    public float updateInterval = 1.0f;
    double lastInterval;
    int frames = 0;
    string display;
    void Start()
    {
        useGUILayout = false;
        lastInterval = Time.realtimeSinceStartup;
        frames = 0;
    }

    void OnGUI()
    {
        GUI.matrix = Matrix4x4.Scale(Vector3.one * 2);
        var rect = new Rect(5, 5, 1000, 20);
        GUI.color = Color.black;
        rect.x += 1;
        rect.y += 1;
        GUI.Label(rect, display);
        GUI.color = Color.white;
        rect.x -= 1;
        rect.y -= 1;
        GUI.Label(rect, display);
    }

    void Update()
    {
        ++frames;
        float timeNow = Time.realtimeSinceStartup;
        if (timeNow > lastInterval + updateInterval)
        {
            var ms = (timeNow - lastInterval) / frames * 1000.0;
            display = $"Perf: {ms:F1}ms";
            frames = 0;
            lastInterval = timeNow;
        }
    }
}
