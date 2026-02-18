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
        Task.Run(() => WorldStreamerLoop(getCameraPos));
    }
    private void WorldStreamerLoop(Func<Vector3> getCameraPos)
    {
        // Should be 31
        const int viewDistance = 31;

        while (true)
        {
            try
            {
                Vector3 camPos = getCameraPos();
                int pCX = (int)Math.Floor(camPos.X / 16.0f);
                int pCZ = (int)Math.Floor(camPos.Z / 16.0f);

                // 1. GLOBAL UNLOAD CHECK
                foreach (var coord in _voxelWorld.Chunks.Keys)
                {
                    if (Math.Abs(coord.Item1 - pCX) > viewDistance ||
                        Math.Abs(coord.Item2 - pCZ) > viewDistance)
                    {
                        if (_voxelWorld.Chunks.TryRemove(coord, out _))
                        {
                            _unloadQueue.Enqueue(coord);
                            // LOG: Identify exactly which chunk is being sent to the graveyard
                            Console.WriteLine($"[WSL]: Enqueued Unload ({coord.Item1}, {coord.Item2}) | Total in Queue: {_unloadQueue.Count}");
                        }
                    }
                }

                // 2. SPIRAL LOAD
                int x = 0, z = 0;
                int dx = 0, dz = -1;
                int sideLength = (viewDistance * 2) + 1;
                int maxChunks = sideLength * sideLength;
                int loadCount = 0;

                for (int i = 0; i < maxChunks; i++)
                {
                    var coord = (pCX + x, pCZ + z);

                    // RACE CONDITION PROTECTION:
                    // If the chunk is in the unload queue, DO NOT try to load it yet.
                    // We check if any item in the queue matches our target coordinate.
                    bool pendingDelete = _unloadQueue.Any(q => q.x == coord.Item1 && q.z == coord.Item2);

                    if (!_voxelWorld.Chunks.ContainsKey(coord) && !pendingDelete)
                    {
                        var chunk = new Chunk(coord.Item1, coord.Item2, _voxelWorld);
                        if (_voxelWorld.Chunks.TryAdd(coord, chunk))
                        {
                            var n = _voxelWorld.GetNeighbors(coord.Item1, coord.Item2);

                            // Mesh only if neighbors exist
                            if (n.r != null && n.l != null && n.f != null && n.b != null)
                                _onChunkReadyForMeshing(chunk);

                            if (n.r != null) _onChunkReadyForMeshing(n.r);
                            if (n.l != null) _onChunkReadyForMeshing(n.l);
                            if (n.f != null) _onChunkReadyForMeshing(n.f);
                            if (n.b != null) _onChunkReadyForMeshing(n.b);

                            loadCount++;
                        }
                    }

                    if (x == z || (x < 0 && x == -z) || (x > 0 && x == 1 - z))
                    {
                        int temp = dx; dx = -dz; dz = temp;
                    }
                    x += dx; z += dz;

                    // Interruption check
                    if (loadCount >= 10)
                    {
                        Vector3 currentPos = getCameraPos();
                        if ((int)Math.Floor(currentPos.X / 16.0f) != pCX || (int)Math.Floor(currentPos.Z / 16.0f) != pCZ)
                            break;

                        Thread.Sleep(1);
                        loadCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WSL ERROR]: {ex}");
            }

            Thread.Sleep(10);
        }
    }

    public void ProcessUnloadQueue(GL gl, List<RenderChunk> renderChunks)
    {
        while (_unloadQueue.TryDequeue(out var coords))
        {
            lock (renderChunks)
            {
                // We use a predicate to find ALL instances of this chunk in the render list
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
                   // Console.WriteLine($"[MainThread]: Deleted {toRemove.Count} mesh(es) at ({coords.x}, {coords.z})");
                }
                else
                {
                    // This is where your 'trailing' chunks live. 
                    // They aren't in renderChunks, but they might still be in the GPU.
                    Console.WriteLine($"[MainThread]: CRITICAL - Ghost Chunk at ({coords.x}, {coords.z}) was not in the render list!");
                }
            }
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