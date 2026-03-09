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
                    if (Math.Abs(coord.Item1 - pCX) > viewDistance || Math.Abs(coord.Item2 - pCZ) > viewDistance)
                    {
                        if (_voxelWorld.Chunks.TryRemove(coord, out _))
                        {
                            _unloadQueue.Enqueue(coord);
                        }
                    }
                }

                // 2. PRE-CALCULATE SPIRAL COORDS
                // We collect the coordinates we need to check first so we can parallelize them
                var loadTargets = new List<(int x, int z)>();
                int sx = 0, sz = 0, dx = 0, dz = -1;
                int sideLength = (viewDistance * 2) + 1;
                int maxChunks = sideLength * sideLength;

                for (int i = 0; i < maxChunks; i++)
                {
                    loadTargets.Add((pCX + sx, pCZ + sz));
                    if (sx == sz || (sx < 0 && sx == -sz) || (sx > 0 && sx == 1 - sz))
                    {
                        int temp = dx; dx = -dz; dz = temp;
                    }
                    sx += dx; sz += dz;
                }

                // 3. PARALLEL BATCH LOAD
                // This will use all CPU cores to blast through the noise math
                Parallel.ForEach(loadTargets, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, coord =>
                {
                    bool pendingDelete = _unloadQueue.Any(q => q.x == coord.x && q.z == coord.z);

                    if (!_voxelWorld.Chunks.ContainsKey(coord) && !pendingDelete)
                    {
                        var chunk = new Chunk(coord.x, coord.z, _voxelWorld);
                        if (_voxelWorld.Chunks.TryAdd(coord, chunk))
                        {
                            // Note: Meshing callbacks usually need to be handled carefully 
                            // if they touch OpenGL, but since this is just flagging for meshing,
                            // it should be safe if your MeshManager is thread-safe.
                            var n = _voxelWorld.GetNeighbors(coord.x, coord.z);
                            if (n.r != null && n.l != null && n.f != null && n.b != null) _onChunkReadyForMeshing(chunk);
                            if (n.r != null) _onChunkReadyForMeshing(n.r);
                            if (n.l != null) _onChunkReadyForMeshing(n.l);
                            if (n.f != null) _onChunkReadyForMeshing(n.f);
                            if (n.b != null) _onChunkReadyForMeshing(n.b);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WSL ERROR]: {ex}");
            }

            Thread.Sleep(10); // Wait longer between full-world scans
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