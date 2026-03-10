using System.Collections.Concurrent;
using System.Numerics;
using VoxelEngine_Silk.Net_1._0.World;
using Silk.NET.OpenGL;

namespace VoxelEngine_Silk.Net_1._0.Helpers;

public class WorldManager
{
    private VoxelWorld _voxelWorld;
    private ConcurrentQueue<(int x, int z)> _unloadQueue;
    private Action<Chunk> _onChunkReadyForMeshing;

    public WorldManager(VoxelWorld world, ConcurrentQueue<(int x, int z)> unloadQueue, Action<Chunk> meshCallback)
    {
        _voxelWorld = world;
        _unloadQueue = unloadQueue;
        _onChunkReadyForMeshing = meshCallback;
    }

    public void StartStreaming(Func<Vector3> getCameraPos)
    {
        // 1. Get initial position
        Vector3 camPos = getCameraPos();
        int pCX = (int)Math.Floor(camPos.X / 16.0f);
        int pCZ = (int)Math.Floor(camPos.Z / 16.0f);
        const int viewDistance = 31;

        // 2. IMMEDIATE COLD START (The Burst)
        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                var coord = (pCX + x, pCZ + z);
                if (!_voxelWorld.Chunks.ContainsKey(coord))
                {
                    var chunk = new Chunk(coord.Item1, coord.Item2, _voxelWorld);
                    if (_voxelWorld.Chunks.TryAdd(coord, chunk))
                    {
                        _onChunkReadyForMeshing(chunk);
                    }
                }
            }
        }

        // 3. Start the continuous background maintenance
        Task.Factory.StartNew(() => WorldStreamerLoop(getCameraPos), TaskCreationOptions.LongRunning);
    }

    public void ProcessUnloadQueue(GL gl, List<RenderChunk> renderChunks)
    {
        while (_unloadQueue.TryDequeue(out var coords))
        {
            // 1. ATOMIC REMOVAL
            // We remove it from the dictionary here, on the Main Thread.
            // This ensures no background thread is currently starting a new task for it.
            if (_voxelWorld.Chunks.TryRemove(coords, out var chunk))
            {
                lock (renderChunks)
                {
                    // 2. Identify all meshes associated with these coordinates
                    // We use .x and .z here because your WorldManager snippet uses those names
                    var toRemove = renderChunks.Where(rc =>
                        (int)Math.Floor(rc.WorldPosition.X / 16.0f) == coords.x &&
                        (int)Math.Floor(rc.WorldPosition.Z / 16.0f) == coords.z).ToList();

                    if (toRemove.Count > 0)
                    {
                        foreach (var rc in toRemove)
                        {
                            MeshManager.DeleteChunkMesh(gl, rc);
                            renderChunks.Remove(rc);
                        }
                    }
                    else
                    {
                        // If it wasn't in the render list yet, it was likely still in the loading queue.
                        // By removing it from _voxelWorld.Chunks above, the worker thread will 
                        // eventually see it's gone and skip the upload.
                    }
                }
            }
        }
    }

    private void WorldStreamerLoop(Func<Vector3> getCameraPos)
    {
        const int viewDistance = 31;
        // HYSTERESIS: Load at 31, but don't even queue for unload until 40.
        // This stops the "ping-pong" effect when moving in circles.
        const int unloadDistance = 40;

        Vector3 lastScanPos = new Vector3(float.MinValue);
        const float moveThreshold = 8.0f;

        while (true)
        {
            try
            {
                Vector3 camPos = getCameraPos();
                if (Vector3.Distance(camPos, lastScanPos) < moveThreshold)
                {
                    Thread.Sleep(100);
                    continue;
                }

                lastScanPos = camPos;
                int pCX = (int)Math.Floor(camPos.X / 16.0f);
                int pCZ = (int)Math.Floor(camPos.Z / 16.0f);

                // 1. DISCOVERY
                Parallel.For(-viewDistance, viewDistance + 1, x =>
                {
                    for (int z = -viewDistance; z <= viewDistance; z++)
                    {
                        var coord = (pCX + x, pCZ + z);
                        if (!_voxelWorld.Chunks.ContainsKey(coord))
                        {
                            var chunk = new Chunk(coord.Item1, coord.Item2, _voxelWorld);
                            if (_voxelWorld.Chunks.TryAdd(coord, chunk))
                            {
                                _onChunkReadyForMeshing(chunk);
                            }
                        }
                    }
                });

                // 2. QUEUE FOR UNLOAD
                // We only add to the queue here. We DO NOT remove from the dictionary.
                // Removal happens in ProcessUnloadQueue on the Main Thread.
                foreach (var chunkPos in _voxelWorld.Chunks.Keys)
                {
                    if (Math.Abs(chunkPos.Item1 - pCX) > unloadDistance ||
                        Math.Abs(chunkPos.Item2 - pCZ) > unloadDistance)
                    {
                        _unloadQueue.Enqueue(chunkPos);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WSL Error]: {ex.Message}");
            }

            Thread.Sleep(100);
        }
    }

    public void UpdateDirtyChunks()
    {
        foreach (var chunk in _voxelWorld.Chunks.Values)
        {
            if (chunk.IsDirty)
            {
                _onChunkReadyForMeshing(chunk);
                chunk.IsDirty = false;
            }
        }
    }

}