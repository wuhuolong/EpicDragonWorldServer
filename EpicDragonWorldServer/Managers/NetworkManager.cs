﻿using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

/**
 * @author Pantelis Andrianakis
 */
public class NetworkManager
{
    static readonly object taskLock = new object(); // Task lock.
    static readonly List<Task> connections = new List<Task>(); // Pending connections.

    internal static void Init()
    {
        new NetworkManager().StartListener().Wait();
    }

    // The core server task.
    async Task StartListener()
    {
        TcpListener tcpListener = TcpListener.Create(Config.SERVER_PORT);
        tcpListener.Start();
        LogManager.Log("Listening on port " + Config.SERVER_PORT + ".");
        while (true)
        {
            TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
            Task task = StartHandleConnectionAsync(tcpClient);
            // If already faulted, re-throw any error on the calling context.
            if (task.IsFaulted)
            {
                task.Wait();
            }
        }
    }

    // Register and handle the connection.
    async Task StartHandleConnectionAsync(TcpClient tcpClient)
    {
        // Start the new connection task.
        Task connectionTask = HandleConnectionAsync(tcpClient);

        // Add it to the list of pending task.
        lock (taskLock)
        {
            connections.Add(connectionTask);
        }

        // Catch all errors of HandleConnectionAsync.
        try
        {
            await connectionTask;
            // We may be on another thread after "await".
        }
        catch (Exception e)
        {
            // Log the error.
            LogManager.Log(e.ToString());
        }
        finally
        {
            // Remove pending task.
            lock (taskLock)
            {
                connections.Remove(connectionTask);
            }
        }
    }

    // Handle new connection.
    async Task HandleConnectionAsync(TcpClient tcpClient)
    {
        // Continue asynchronously on another threads.
        await Task.Yield();

        // Initialize game client.
        NetworkStream networkStream = tcpClient.GetStream();
        GameClient gameClient = new GameClient(networkStream, tcpClient.Client.RemoteEndPoint.ToString());

        byte[] bufferLength = new byte[2]; // We use 2 bytes for short value.
        byte[] bufferData;
        short length; // Since we use short value, max length should be 32767.

        while (!(tcpClient.Client.Poll(1, SelectMode.SelectRead) && !networkStream.DataAvailable))
        {
            try
            {
                // Get packet data length.
                await networkStream.ReadAsync(bufferLength, 0, 2);
                length = BitConverter.ToInt16(bufferLength, 0);
                // Get packet data.
                bufferData = new byte[length];
                await networkStream.ReadAsync(bufferData, 0, length);
                // Handle packet.
                RecievablePacketManager.Handle(gameClient, new ReceivablePacket(Encryption.Decrypt(bufferData)));
            }
            catch (Exception)
            {
                // Connection closed from client side.
            }
        }

        // Disconnected.
        WorldManager.RemoveClient(gameClient);
    }
}
