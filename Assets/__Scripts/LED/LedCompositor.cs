using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SaturnGame.UI;
using UnityEngine;
using USBIntLEDDll;

namespace SaturnGame.LED
{
/// <summary>
/// If there's any trouble with LED displaying, check execution order. This must be LAST!
/// </summary>
public class LedCompositor : PersistentSingleton<LedCompositor>
{
    public List<LedDrawable> LedDrawableQueue;
    [SerializeField] private RingDebugManager ringDebugManager;

    [SerializeField] private bool useNativeLedImplementation;
    [SerializeField] [Range(0, 1)] private float ledBrightness;

    [SerializeField] private Color32[,] ledValues = new Color32[8,60];

    private readonly LedData ledData = new()
    {
        unitCount = 60 * 8,
        rgbaValues = new Color32[480],
    };

    private bool sendingLedData;

    private NativeLedOutput nativeLedOutput;

    private void Start()
    {
        if (useNativeLedImplementation)
        {
            nativeLedOutput = new();
            nativeLedOutput.Init();
        }
        else
            USBIntLED.Safe_USBIntLED_Init();
    }

    private byte AdjustBrightness(byte value)
    {
        return (byte)(value * ledBrightness);
    }

    private void ClearCanvas(Color32 color)
    {
        for (int i = 0; i < 8; i++)
        for (int j = 0; j < 60; j++)
        {
            ledValues[i, j] = color;
        }
    }

    private async Awaitable SetCabLeds()
    {
        // makeshift lock - we only grab the lock while on the main thread, and we only check it from the main thread
        // So I think it should be safe from race conditions.
        if (sendingLedData) return;
        sendingLedData = true;

        try
        {
            // write to LEDs
            // LedData 0 is anglePos 45, then LedData is increasing CW (in the negative direction)
            for (int i = 0; i < 8; i++)
            for (int j = 0; j < 60; j++)
            {
                int anglePos = SaturnMath.Modulo(44 - i, 60);
                ledData.rgbaValues[i * 8 + j] = new(
                    AdjustBrightness(ledValues[i, j].r),
                    AdjustBrightness(ledValues[i, j].g),
                    AdjustBrightness(ledValues[i, j].b), 0);
            }

            await Awaitable.BackgroundThreadAsync();

            if (useNativeLedImplementation)
                nativeLedOutput.SetLeds(ledData.rgbaValues);
            else
                USBIntLED.Safe_USBIntLED_set(0, ledData);

            // wait for LED reset low period (at least 280 microseconds)
            // 0.5ms (500 us) seemed a bit unstable but this might have actually just been broken.
            // Awaitable.WaitForSecondsAsync only works on the main thread, but it's really slow to switch back to the
            // main thread (takes at least a frame). So just use Thread.Sleep for 1ms and then release the "lock"
            Thread.Sleep(1);
        }
        finally
        {
            sendingLedData = false;
        }
    }

    private async void FixedUpdate()
    {
        // Fill all LEDs with black first
        ClearCanvas(Color.black);

        foreach (LedDrawable drawable in LedDrawableQueue.Where(x => x.Enabled).OrderBy(x => x.Layer))
            drawable.Draw(ref ledValues);

        LedDrawableQueue.Clear();

        // Send data to LED boards / debug display.

        if (ringDebugManager != null) ringDebugManager.UpdateColors(ledValues);

        await SetCabLeds();
    }

    private void Update()
    {
        // Toggle RingDebug when F2 is pressed
        if (Input.GetKeyDown(KeyCode.F2)) ringDebugManager.ToggleVisibility();
    }

    protected override async void OnApplicationQuit()
    {
        base.OnApplicationQuit();
        ClearCanvas(Color.black);
        await SetCabLeds();
    }
}
}
