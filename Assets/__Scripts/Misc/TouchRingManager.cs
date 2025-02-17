using System;
using System.Collections.Generic;
using System.IO.Ports;
using JetBrains.Annotations;
using SaturnGame.RhythmGame;
using UnityEngine;

namespace SaturnGame
{
/// <summary>
/// TouchRingManager manages the serial connection with the touch ring (input only, not LEDs).
/// </summary>
public class TouchRingManager : PersistentSingleton<TouchRingManager>, IInputProvider
{
    public TouchState CurrentTouchState = TouchState.CreateNew();


    // Warning: Please only use BaseStream to read/write. The .NET SerialPort implementation has several issues,
    // especially regarding its internal buffer/cache. This can all be avoided if you use BaseStream directly and
    // never use any of the Read/Write methods on SerialPort itself. I think the mono implementation is actually
    // basically okay, but let's be cautious and consistent.
    // See https://sparxeng.com/blog/software/must-use-net-system-io-ports-serialport
    // Also, the mono implementation does not implement events, so you cannot use .DataReceived, even if it were safe.
    // https://www.mono-project.com/archived/howtosystemioports/#limitations
    private SerialPort leftRingPort;
    private SerialPort rightRingPort;

    private readonly bool[,] leftTouchData = new bool[30, 4];
    private readonly bool[,] rightTouchData = new bool[30, 4];

    private enum SerialCommand
    {
        // This is not a complete list, but includes only the commands Saturn uses to operate.
        // LilyConsole has a more complete list.
        TouchData = 0x81,
        SetThresholds = 0x94,
        GetSyncBoardVer = 0xA0,
        GetUnitBoardVer = 0xA8,
        StartAutoScan = 0xC9,
    }

    // Touch controller code is adapted from yellowberryHN/LilyConsole
    private async Awaitable InitializeTouchController()
    {
        SerialPort leftPort = new("COM4", 115200);
        SerialPort rightPort = new("COM3", 115200);
        // Start both in parallel, and await afterwards.
        Awaitable leftAwaitable = InitializeSerialPort(leftPort, (byte)'L');
        Awaitable rightAwaitable = InitializeSerialPort(rightPort, (byte)'R');
        await leftAwaitable;
        leftRingPort = leftPort;
        await rightAwaitable;
        rightRingPort = rightPort;
    }

    private static async Awaitable InitializeSerialPort([NotNull] SerialPort port, byte side)
    {
        if (!port.IsOpen) port.Open();
        Debug.Log($"Port {(char)side}: port opened.");

        // In milliseconds.
        // During streaming mode, board sends data every 8ms.
        port.ReadTimeout = 1000;
        port.WriteTimeout = 1000;

        // Send once to get board to shut up.
        await SendData(port, new[] { (byte)SerialCommand.GetSyncBoardVer });
        // Wait to make sure we've received all incoming bytes and they are all discarded.
        // This WaitForSecondsAsync is a bit of a hack - ideally we just scan forward through received bytes until we
        // find the right response.
        await Awaitable.WaitForSecondsAsync(0.020f);
        port.DiscardInBuffer();
        Debug.Log($"Port {(char)side}: shut up!");

        // Get unit board versions
        await SendData(port, new[] { (byte)SerialCommand.GetUnitBoardVer });
        byte[] unitVersionResponse = await ReadData(port, 45);
        byte reportedSide = unitVersionResponse[7];
        if (reportedSide != side)
            throw new Exception("Sync Board disagrees which side it is! Wanted {(char)side}, got {reportedSide}.");
        Debug.Log($"Port {(char)side}: got unit board version.");

        const byte onThreshold = 17;
        const byte offThreshold = 12;
        // TODO: don't hardcode checksum
        byte[] thresholdCommand =
        {
            (byte)SerialCommand.SetThresholds, onThreshold, onThreshold, onThreshold, onThreshold, onThreshold,
            onThreshold, offThreshold, offThreshold, offThreshold, offThreshold, offThreshold, offThreshold, 0x14,
        };
        await SendData(port, thresholdCommand);
        byte[] thresholdResponse = await ReadData(port, 3);
        Debug.Log($"Threshold ({onThreshold}/{offThreshold}) response:");
        Debug.Log(BitConverter.ToString(thresholdResponse));

        // Start touch stream
        await SendData(port,
            new byte[] { (byte)SerialCommand.StartAutoScan, 0x7F, 0x3F, 0x64, 0x28, 0x44, 0x3B, 0x3A });
        byte[] ack = await ReadData(port, 3);
        if (ack[0] != (byte)SerialCommand.StartAutoScan)
            throw new Exception("Start Scan message was not acknowledged.");
        Debug.Log($"Port {(char)side}: started touch stream.");
    }

    private static async Awaitable SendData([NotNull] SerialPort port, [NotNull] byte[] data)
    {
        await port.BaseStream.WriteAsync(data, 0, data.Length);
    }

    // Taken verbatim from LilyConsole
    /// <summary>
    /// Validates the checksum on the end of a given full payload.
    /// </summary>
    /// <param name="packet">The bytes of the payload to be validated.</param>
    /// <returns>The validity of the checksum</returns>
    private static bool ValidChecksum([NotNull] IReadOnlyList<byte> packet)
    {
        byte chk = 0x80;
        for (int i = 0; i < packet.Count - 1; i++)
            chk ^= packet[i];
        return packet[^1] == chk;
    }

    private static async Awaitable<byte[]> ReadData(SerialPort port, int size)
    {
        byte[] buffer = new byte[size];
        int bytesRead = 0;
        while (bytesRead < size)
            bytesRead += await port.BaseStream.ReadAsync(buffer, bytesRead, buffer.Length - bytesRead);
        if (!ValidChecksum(buffer))
        {
            // TODO: some kind of better error handling here - e.g. reboot the touch connection.
            throw new Exception("Bad serial checksum!");
        }

        return buffer;
    }

    // Write to a 30x4 array of segments in the order given by the sync board.
    // The translation from the first index to anglePos depends on which side the board is on.
    private static void ReadTouchDataNonBlocking([NotNull] SerialPort port, bool[,] dest)
    {
        // TODO: avoid allocation here
        byte[] buffer = new byte[36];
        bool hasData = false;
        while (port.BytesToRead >= buffer.Length)
        {
            int readCommandByte = port.BaseStream.Read(buffer, 0, 1);
            if (readCommandByte != 1) Debug.Log("Didn't successfully read touch command.");
            if (buffer[0] != (byte)SerialCommand.TouchData)
            {
                Debug.Log($"Found {BitConverter.ToString(new[] { buffer[0] })} instead of a TOUCH_DATA command (81)!");
                continue;
            }

            int readBytes = port.BaseStream.Read(buffer, 1, buffer.Length - 1) + readCommandByte;
            if (readBytes != 36)
            {
                Debug.Log($"read {readBytes} bytes instead of expected 36");
                continue;
            }

            if (!ValidChecksum(buffer))
            {
                // TODO: some kind of better error handling here - e.g. reboot the touch connection.
                throw new Exception("Bad serial checksum!");
            }

            hasData = true;
        }

        if (!hasData) return;

        // Buffer layout:
        //  1 byte : SerialCommand.TOUCH_DATA
        // 24 bytes: 4x 6-byte row
        //           A row represents each panel with a single byte. A row contains 6 panels.
        //           The panel byte representation just uses the 5 least significant bits of the byte.
        //  9 bytes: ???
        //  1 byte : Loop state, increases by 1 every time the sync board sends a frame
        //  1 byte : Checksum, already validated above

        if (buffer[0] != (byte)SerialCommand.TouchData)
        {
            throw new Exception(
                $"Expected to receive TOUCH_DATA, but got {BitConverter.ToString(new[] { buffer[0] })}");
        }

        // PANELS_PER_SIDE * COLS_PER_PANEL = 30
        const int panelsPerSide = 6;
        const int colsPerPanel = 5;
        for (int depth = 0; depth < 4; depth++)
        {
            for (int panel = 0; panel < panelsPerSide; panel++)
            {
                int panelRowData = buffer[1 + depth * panelsPerSide + panel];

                for (int panelColumn = 0; panelColumn < colsPerPanel; panelColumn++)
                {
                    int panelColumnMask = 1 << panelColumn;
                    bool active = (panelRowData & panelColumnMask) != 0;

                    int angleOffset = panel * colsPerPanel + panelColumn;
                    dest[angleOffset, depth] = active;
                }
            }
        }

        // loop state is currently discarded.
    }

    private async void Start()
    {
        await InitializeTouchController();
    }

    private void UpdateSegments([NotNull] bool[,] segments)
    {
        // LEFT
        if (leftRingPort is not null)
        {
            ReadTouchDataNonBlocking(leftRingPort, leftTouchData);
            for (int angleOffset = 0; angleOffset < 30; angleOffset++)
            {
                // touchData 0 is at the top, and then increasing CCW
                int anglePos = SaturnMath.Modulo(angleOffset + 15, 60);

                for (int depthPos = 0; depthPos < 4; depthPos++)
                    segments[anglePos, depthPos] = leftTouchData[angleOffset, 3 - depthPos];
            }
        }

        // Right
        if (rightRingPort is not null)
        {
            ReadTouchDataNonBlocking(rightRingPort, rightTouchData);
            for (int angleOffset = 0; angleOffset < 30; angleOffset++)
            {
                // touchData 0 is at the top, and then increasing CW
                int anglePos = SaturnMath.Modulo(14 - angleOffset, 60);

                for (int depthPos = 0; depthPos < 4; depthPos++)
                    segments[anglePos, depthPos] = rightTouchData[angleOffset, 3 - depthPos];
            }
        }
    }

    private void Update()
    {
        TouchState.StealAndUpdateSegments(ref CurrentTouchState, UpdateSegments);
    }

    // Note: TouchRingManager's script execution order should be before anything calling GetCurrentTouchState(), to
    // make sure the latest touch state has been read. Currently, (2024-06-24) the only consumer is InputManager.
    public TouchState GetCurrentTouchState()
    {
        return CurrentTouchState;
    }
}
}
