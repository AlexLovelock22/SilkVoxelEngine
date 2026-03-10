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

    private void WorldStreamerLoop(Func<Vector3> getCameraPos)
    {
        const int viewDistance = 31;
        int lastCX = int.MinValue;
        int lastCZ = int.MinValue;

        while (true)
        {
            try
            {
                Vector3 camPos = getCameraPos();
                int pCX = (int)Math.Floor(camPos.X / 16.0f);
                int pCZ = (int)Math.Floor(camPos.Z / 16.0f);

                // 1. UNLOAD (Check every cycle)
                foreach (var coord in _voxelWorld.Chunks.Keys)
                {
                    if (Math.Abs(coord.Item1 - pCX) > viewDistance || Math.Abs(coord.Item2 - pCZ) > viewDistance)
                    {
                        if (_voxelWorld.Chunks.TryRemove(coord, out _))
                            _unloadQueue.Enqueue(coord);
                    }
                }

                // 2. SPIRAL DISCOVERY
                // We start r=0 every time. If pCX changes mid-loop, we want to know.
                for (int r = 0; r <= viewDistance; r++)
                {
                    // RE-CENTER CHECK: If player moved to a new chunk, abort this spiral and start a new one!
                    Vector3 currentPos = getCameraPos();
                    if ((int)Math.Floor(currentPos.X / 16f) != pCX || (int)Math.Floor(currentPos.Z / 16f) != pCZ)
                        break;

                    for (int x = -r; x <= r; x++)
                    {
                        for (int z = -r; z <= r; z++)
                        {
                            if (Math.Abs(x) != r && Math.Abs(z) != r) continue;

                            var coord = (pCX + x, pCZ + z);
                            if (!_voxelWorld.Chunks.ContainsKey(coord))
                            {
                                var chunk = new Chunk(coord.Item1, coord.Item2, _voxelWorld);
                                if (_voxelWorld.Chunks.TryAdd(coord, chunk))
                                    _onChunkReadyForMeshing(chunk);
                            }
                        }
                    }
                    if (r % 5 == 0) Thread.Sleep(1);
                }
            }
            catch (Exception ex) { Console.WriteLine($"[WSL ERROR]: {ex}"); }
            Thread.Sleep(10); // High frequency check
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