using UnityEngine;
using System.Collections;

public class FPSCounter : MonoBehaviour
{
    public int AverageFPS { get; private set; }
    public int HiFPS { get; private set; }
    public int LowFPS { get; private set; }

    public int FrameRange;

    int[] fpsBuffer;
    int fpsBufferIndex;

    void Awake()
    {
        HiFPS = 0;
        LowFPS = 99;
    }

    // Update is called once per frame
    void Update()
    {
        if (fpsBuffer == null || fpsBuffer.Length != FrameRange)
        {
            Initialize();
        }
        UpdateBuffer();
        CaluateFPS();
    }

    void Initialize()
    {
        if (FrameRange <= 0)
        {
            FrameRange = 1;
        }

        fpsBuffer = new int[FrameRange];
        fpsBufferIndex = 0;
    }

    void UpdateBuffer()
    {
        int fps = (int)(1.0f / Time.unscaledDeltaTime);
        fpsBuffer[fpsBufferIndex++] = fps;

        if (fpsBufferIndex >= FrameRange)
        {
            fpsBufferIndex = 0;
        }
    }

    void CaluateFPS()
    {
        int sum = 0;
        int highest = 0;
        int lowest = int.MaxValue;

        for (int index = 0; index < FrameRange; index++)
        {
            int fps = fpsBuffer[index];
            sum += fps;

            if (fps > highest)
            {
                highest = fps;
            }
            else if (fps < lowest)
            {
                lowest = fps;
            }
        }

        AverageFPS = sum / FrameRange;
        HiFPS = highest;
        LowFPS = lowest;
    }
}
