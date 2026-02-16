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

                HashSet<(int, int)> visibleCoords = new HashSet<(int, int)>();

                // Spiral logic to find all coords within viewDistance square
                int x = 0, z = 0;
                int dx = 0, dz = -1;
                int sideLength = (viewDistance * 2) + 1;
                int maxChunks = sideLength * sideLength;

                for (int i = 0; i < maxChunks; i++)
                {
                    visibleCoords.Add((pCX + x, pCZ + z));

                    if (x == z || (x < 0 && x == -z) || (x > 0 && x == 1 - z))
                    {
                        int temp = dx; dx = -dz; dz = temp;
                    }
                    x += dx; z += dz;
                }

                // 1. UNLOAD (Only if it's truly outside the set)
                int unloadCount = 0;
                foreach (var coord in _voxelWorld.Chunks.Keys)
                {
                    if (!visibleCoords.Contains(coord))
                    {
                        if (_voxelWorld.Chunks.TryRemove(coord, out _))
                        {
                            _unloadQueue.Enqueue(coord);
                            unloadCount++;
                        }
                    }
                }
                if (unloadCount > 0) Console.WriteLine($"[WSL] Unloaded {unloadCount} chunks.");

                // 2. LOAD
                int loadCount = 0;
                foreach (var coord in visibleCoords)
                {
                    if (!_voxelWorld.Chunks.ContainsKey(coord))
                    {
                        var chunk = new Chunk(coord.Item1, coord.Item2, _voxelWorld);
                        if (_voxelWorld.Chunks.TryAdd(coord, chunk))
                        {
                            _onChunkReadyForMeshing(chunk);

                            // Remesh neighbors to fix the "diagonal" culling walls
                            UpdateNeighborIfExists(coord.Item1 + 1, coord.Item2);
                            UpdateNeighborIfExists(coord.Item1 - 1, coord.Item2);
                            UpdateNeighborIfExists(coord.Item1, coord.Item2 + 1);
                            UpdateNeighborIfExists(coord.Item1, coord.Item2 - 1);
                            loadCount++;
                        }
                    }
                    // Throttle loading so we don't drop frames
                    if (loadCount > 5) { Thread.Sleep(1); loadCount = 0; }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WSL ERROR]: {ex}");
            }

            Thread.Sleep(50); // Increased sleep to prevent "flickering"
        }
    }

    private void UpdateNeighborIfExists(int cx, int cz)
    {
        if (_voxelWorld.Chunks.TryGetValue((cx, cz), out var neighbor))
        {
            _onChunkReadyForMeshing(neighbor);
        }
    }

    public void ProcessUnloadQueue(GL gl, List<RenderChunk> renderChunks)
    {
        while (_unloadQueue.TryDequeue(out var coords))
        {
            lock (renderChunks)
            {
                var existing = renderChunks.Find(rc =>
                    (int)Math.Floor(rc.WorldPosition.X / 16.0f) == coords.x &&
                    (int)Math.Floor(rc.WorldPosition.Z / 16.0f) == coords.z);

                if (existing != null)
                {
                    MeshManager.DeleteChunkMesh(gl, existing);
                    renderChunks.Remove(existing);
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
                chunk.IsDirty = false;
                _onChunkReadyForMeshing(chunk);
            }
        }
    }

}