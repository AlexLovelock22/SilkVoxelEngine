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
        const int viewDistance = 40;

        while (true)
        {
            // Fix: Use the delegate to get the player position
            Vector3 camPos = getCameraPos();
            int pCX = (int)Math.Floor(camPos.X / 16.0f);
            int pCZ = (int)Math.Floor(camPos.Z / 16.0f);

            HashSet<(int, int)> visibleCoords = new HashSet<(int, int)>();

            // YOUR SPIRAL LOGIC RESTORED
            int x = 0, z = 0;
            int dx = 0, dz = -1;
            int sideLength = viewDistance * 2;
            int maxChunks = sideLength * sideLength;

            for (int i = 0; i < maxChunks; i++)
            {
                int currentX = pCX + x;
                int currentZ = pCZ + z;

                float dist = Vector2.Distance(new Vector2(currentX, currentZ), new Vector2(pCX, pCZ));
                if (dist <= viewDistance)
                {
                    visibleCoords.Add((currentX, currentZ));
                }

                if (x == z || (x < 0 && x == -z) || (x > 0 && x == 1 - z))
                {
                    int temp = dx; dx = -dz; dz = temp;
                }
                x += dx; z += dz;
            }

            // Unloading
            foreach (var coord in _voxelWorld.Chunks.Keys)
            {
                if (!visibleCoords.Contains(coord))
                {
                    if (_voxelWorld.Chunks.TryRemove(coord, out _))
                    {
                        _unloadQueue.Enqueue(coord);
                    }
                }
            }

            // Loading & Meshing
            foreach (var coord in visibleCoords)
            {
                if (!_voxelWorld.Chunks.ContainsKey(coord))
                {
                    var chunk = new Chunk(coord.Item1, coord.Item2, _voxelWorld);

                    if (_voxelWorld.Chunks.TryAdd(coord, chunk))
                    {
                        var n = _voxelWorld.GetNeighbors(coord.Item1, coord.Item2);

                        // Fix: Use the callback instead of the old QueueChunkForMeshing method
                        if (n.r != null && n.l != null && n.f != null && n.b != null)
                            _onChunkReadyForMeshing(chunk);

                        if (n.r != null) _onChunkReadyForMeshing(n.r);
                        if (n.l != null) _onChunkReadyForMeshing(n.l);
                        if (n.f != null) _onChunkReadyForMeshing(n.f);
                        if (n.b != null) _onChunkReadyForMeshing(n.b);
                    }
                    Thread.Sleep(0);
                }
            }

            Thread.Sleep(200);
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