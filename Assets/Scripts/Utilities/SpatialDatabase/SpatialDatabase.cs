using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public interface ISpatialQueryCollector
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnVisitCell(in SpatialDatabaseCell cell, in UnsafeList<SpatialDatabaseElement> elements, out bool shouldEarlyExit);
}

[InternalBufferCapacity(0)]
[StructLayout(LayoutKind.Explicit)]
public struct SpatialDatabaseCell : IBufferElementData
{
    [FieldOffset(0)]
    public int UncappedElementsCount;

    [FieldOffset(4)]
    public int StartIndex;
    [FieldOffset(8)]
    public int ElementsCapacity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetValidElementsCount()
    {
        return math.min(math.max(0, UncappedElementsCount), ElementsCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetExcessElementsCount()
    {
        return math.max(0, math.abs(UncappedElementsCount) - ElementsCapacity);
    }
}

[InternalBufferCapacity(0)]
public struct SpatialDatabaseElement : IBufferElementData, IComparable<SpatialDatabaseElement>
{
    public Entity Entity;
    public float3 Position;
    public byte Team;
    public byte Type;

    public int CompareTo(SpatialDatabaseElement other)
    {
        return Entity.CompareTo(other.Entity);
    }
}

public unsafe struct CachedSpatialDatabase
{
    public Entity SpatialDatabaseEntity;
    public ComponentLookup<SpatialDatabase> SpatialDatabaseLookup;
    public BufferLookup<SpatialDatabaseCell> CellsBufferLookup;
    public BufferLookup<SpatialDatabaseElement> ElementsBufferLookup;

    public bool _IsInitialized;
    public SpatialDatabase _SpatialDatabase;
    public UnsafeList<SpatialDatabaseCell> _SpatialDatabaseCells;
    public UnsafeList<SpatialDatabaseElement> _SpatialDatabaseElements;

    public void CacheData()
    {
        if (!_IsInitialized)
        {
            _SpatialDatabase = SpatialDatabaseLookup[SpatialDatabaseEntity];
            DynamicBuffer<SpatialDatabaseCell> cellsBuffer = CellsBufferLookup[SpatialDatabaseEntity];
            DynamicBuffer<SpatialDatabaseElement> elementsBuffer = ElementsBufferLookup[SpatialDatabaseEntity];
            _SpatialDatabaseCells = new UnsafeList<SpatialDatabaseCell>((SpatialDatabaseCell*)cellsBuffer.GetUnsafePtr(), cellsBuffer.Length);
            _SpatialDatabaseElements = new UnsafeList<SpatialDatabaseElement>((SpatialDatabaseElement*)elementsBuffer.GetUnsafePtr(), elementsBuffer.Length);
            _IsInitialized = true;
        }
    }
}


public unsafe struct CachedSpatialDatabaseUnsafeParallel
{
    public Entity SpatialDatabaseEntity;
    [NativeDisableParallelForRestriction]
    public ComponentLookup<SpatialDatabase> SpatialDatabaseLookup;
    [NativeDisableParallelForRestriction]
    public BufferLookup<SpatialDatabaseCell> CellsBufferLookup;
    [NativeDisableParallelForRestriction]
    public BufferLookup<SpatialDatabaseElement> ElementsBufferLookup;

    public bool _IsInitialized;
    public SpatialDatabase _SpatialDatabase;
    public UnsafeList<SpatialDatabaseCell> _SpatialDatabaseCells;
    public UnsafeList<SpatialDatabaseElement> _SpatialDatabaseElements;

    public void CacheData()
    {
        if (!_IsInitialized)
        {
            _SpatialDatabase = SpatialDatabaseLookup[SpatialDatabaseEntity];
            DynamicBuffer<SpatialDatabaseCell> cellsBuffer = CellsBufferLookup[SpatialDatabaseEntity];
            DynamicBuffer<SpatialDatabaseElement> elementsBuffer = ElementsBufferLookup[SpatialDatabaseEntity];
            _SpatialDatabaseCells = new UnsafeList<SpatialDatabaseCell>((SpatialDatabaseCell*)cellsBuffer.GetUnsafePtr(), cellsBuffer.Length);
            _SpatialDatabaseElements = new UnsafeList<SpatialDatabaseElement>((SpatialDatabaseElement*)elementsBuffer.GetUnsafePtr(), elementsBuffer.Length);
            _IsInitialized = true;
        }
    }
}

public unsafe struct CachedSpatialDatabaseRO
{
    public Entity SpatialDatabaseEntity;
    [ReadOnly]
    public ComponentLookup<SpatialDatabase> SpatialDatabaseLookup;
    [ReadOnly]
    public BufferLookup<SpatialDatabaseCell> CellsBufferLookup;
    [ReadOnly]
    public BufferLookup<SpatialDatabaseElement> ElementsBufferLookup;

    public bool _IsInitialized;
    public SpatialDatabase _SpatialDatabase;
    public UnsafeList<SpatialDatabaseCell> _SpatialDatabaseCells;
    public UnsafeList<SpatialDatabaseElement> _SpatialDatabaseElements;

    public void CacheData()
    {
        if (!_IsInitialized)
        {
            _SpatialDatabase = SpatialDatabaseLookup[SpatialDatabaseEntity];
            DynamicBuffer<SpatialDatabaseCell> cellsBuffer = CellsBufferLookup[SpatialDatabaseEntity];
            DynamicBuffer<SpatialDatabaseElement> elementsBuffer = ElementsBufferLookup[SpatialDatabaseEntity];
            _SpatialDatabaseCells = new UnsafeList<SpatialDatabaseCell>((SpatialDatabaseCell*)cellsBuffer.GetUnsafeReadOnlyPtr(), cellsBuffer.Length);
            _SpatialDatabaseElements = new UnsafeList<SpatialDatabaseElement>((SpatialDatabaseElement*)elementsBuffer.GetUnsafeReadOnlyPtr(), elementsBuffer.Length);
            _IsInitialized = true;
        }
    }
}

public struct SpatialDatabase : IComponentData
{
    public UniformOriginGrid Grid;

    public const float ElementsCapacityGrowFactor = 2f;

    public static void Initialize(float halfExtents, int subdivisions, int cellEntriesCapacity,
        ref SpatialDatabase spatialDatabase, ref DynamicBuffer<SpatialDatabaseCell> cellsBuffer,
        ref DynamicBuffer<SpatialDatabaseElement> storageBuffer)
    {
        cellsBuffer.Clear();
        storageBuffer.Clear();
        cellsBuffer.Capacity = 16;
        storageBuffer.Capacity = 16;

        spatialDatabase.Grid = new UniformOriginGrid(halfExtents, subdivisions);

        cellsBuffer.Resize(spatialDatabase.Grid.CellCount, NativeArrayOptions.ClearMemory);
        storageBuffer.Resize(spatialDatabase.Grid.CellCount * cellEntriesCapacity, NativeArrayOptions.ClearMemory);

        for (int i = 0; i < cellsBuffer.Length; i++)
        {
            SpatialDatabaseCell cell = cellsBuffer[i];
            cell.StartIndex = i * cellEntriesCapacity;
            cell.UncappedElementsCount = 0;
            cell.ElementsCapacity = cellEntriesCapacity;
            cellsBuffer[i] = cell;
        }
    }

    public static void ClearAndResize(ref DynamicBuffer<SpatialDatabaseCell> cellsBuffer,
        ref DynamicBuffer<SpatialDatabaseElement> storageBuffer)
    {
        int totalDesiredStorage = 0;
        for (int i = 0; i < cellsBuffer.Length; i++)
        {
            SpatialDatabaseCell cell = cellsBuffer[i];
            cell.StartIndex = totalDesiredStorage;

            cell.ElementsCapacity = math.select(cell.ElementsCapacity,
                (int)math.ceil((cell.ElementsCapacity + cell.GetExcessElementsCount()) * ElementsCapacityGrowFactor),
                cell.GetExcessElementsCount() > 0);
            totalDesiredStorage += cell.ElementsCapacity;

            cell.UncappedElementsCount = 0;

            cellsBuffer[i] = cell;
        }

        storageBuffer.Resize(totalDesiredStorage, NativeArrayOptions.ClearMemory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void AddToDataBaseSingleThread(in SpatialDatabase spatialDatabase,
        ref UnsafeList<SpatialDatabaseCell> cellsBuffer, ref UnsafeList<SpatialDatabaseElement> storageBuffer,
        in SpatialDatabaseElement element)
    {
        int cellIndex = UniformOriginGrid.GetCellIndex(in spatialDatabase.Grid, element.Position);
        if (cellIndex >= 0)
        {
            ref SpatialDatabaseCell cellRef = 
                ref UnsafeUtility.ArrayElementAsRef<SpatialDatabaseCell>(cellsBuffer.Ptr, cellIndex);

            int addIndex = cellRef.UncappedElementsCount;
            cellRef.UncappedElementsCount++;

            if (addIndex < cellRef.ElementsCapacity)
            {
                storageBuffer[cellRef.StartIndex + addIndex] = element;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void AddToDataBaseParallel(in SpatialDatabase spatialDatabase,
        ref UnsafeList<SpatialDatabaseCell> cellsBuffer, ref UnsafeList<SpatialDatabaseElement> storageBuffer,
        in SpatialDatabaseElement element)
    {
        int cellIndex = UniformOriginGrid.GetCellIndex(in spatialDatabase.Grid, element.Position);
        if (cellIndex >= 0)
        {
            SpatialDatabaseCell* cellPtr = cellsBuffer.Ptr + (long)cellIndex;

            int addIndex = Interlocked.Increment(ref UnsafeUtility.AsRef<int>(cellPtr)) - 1;

            if (addIndex < cellPtr->ElementsCapacity)
            {
                storageBuffer[cellPtr->StartIndex + addIndex] = element;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void QueryAABB<T>(in SpatialDatabase spatialDatabase,
        in DynamicBuffer<SpatialDatabaseCell> cellsBuffer, in DynamicBuffer<SpatialDatabaseElement> elementsBuffer,
        float3 center, float3 halfExtents, ref T collector)
        where T : unmanaged, ISpatialQueryCollector
    {
        UnsafeList<SpatialDatabaseCell> cells =
            new UnsafeList<SpatialDatabaseCell>((SpatialDatabaseCell*)cellsBuffer.GetUnsafeReadOnlyPtr(),
                cellsBuffer.Length);
        UnsafeList<SpatialDatabaseElement> elements =
            new UnsafeList<SpatialDatabaseElement>((SpatialDatabaseElement*)elementsBuffer.GetUnsafeReadOnlyPtr(),
                elementsBuffer.Length);
        QueryAABB(in spatialDatabase, in cells, in elements, center, halfExtents, ref collector);
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void QueryAABB<T>(in SpatialDatabase spatialDatabase,
        in UnsafeList<SpatialDatabaseCell> cellsBuffer, in UnsafeList<SpatialDatabaseElement> elementsBuffer,
        float3 center, float3 halfExtents, ref T collector)
        where T : unmanaged, ISpatialQueryCollector
    {
        float3 aabbMin = center - halfExtents;
        float3 aabbMax = center + halfExtents;
        UniformOriginGrid grid = spatialDatabase.Grid;
        if (UniformOriginGrid.GetAABBMinMaxCoords(in grid, aabbMin, aabbMax, out int3 minCoords,
                out int3 maxCoords))
        {
            for (int y = minCoords.y; y <= maxCoords.y; y++)
            {
                for (int z = minCoords.z; z <= maxCoords.z; z++)
                {
                    for (int x = minCoords.x; x <= maxCoords.x; x++)
                    {
                        int3 coords = new int3(x, y, z);
                        int cellIndex = UniformOriginGrid.GetCellIndexFromCoords(in grid, coords);
                        SpatialDatabaseCell cell = cellsBuffer[cellIndex];
                        collector.OnVisitCell(in cell, in elementsBuffer,
                            out bool shouldEarlyExit);
                        if (shouldEarlyExit)
                        {
                            return;
                        }
                    }
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe static void QueryAABBCellProximityOrder<T>(in SpatialDatabase spatialDatabase,
        in DynamicBuffer<SpatialDatabaseCell> cellsBuffer, in DynamicBuffer<SpatialDatabaseElement> elementsBuffer,
        float3 center, float3 halfExtents, ref T collector)
        where T : unmanaged, ISpatialQueryCollector
    {
        UnsafeList<SpatialDatabaseCell> cells =
            new UnsafeList<SpatialDatabaseCell>((SpatialDatabaseCell*)cellsBuffer.GetUnsafeReadOnlyPtr(),
                cellsBuffer.Length);
        UnsafeList<SpatialDatabaseElement> elements =
            new UnsafeList<SpatialDatabaseElement>((SpatialDatabaseElement*)elementsBuffer.GetUnsafeReadOnlyPtr(),
                elementsBuffer.Length);
        QueryAABBCellProximityOrder(in spatialDatabase, in cells, in elements, center, halfExtents, ref collector);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void QueryAABBCellProximityOrder<T>(in SpatialDatabase spatialDatabase,
        in UnsafeList<SpatialDatabaseCell> cellsBuffer, in UnsafeList<SpatialDatabaseElement> elementsBuffer,
        float3 center, float3 halfExtents, ref T collector)
        where T : unmanaged, ISpatialQueryCollector
    {
        float3 aabbMin = center - halfExtents;
        float3 aabbMax = center + halfExtents;
        UniformOriginGrid grid = spatialDatabase.Grid;
        if (UniformOriginGrid.GetAABBMinMaxCoords(in grid, aabbMin, aabbMax, out int3 minCoords, out int3 maxCoords))
        {
            int3 sourceCoord = UniformOriginGrid.GetCellCoordsFromPosition(in grid, center);
            int3 highestCoordDistances = math.max(maxCoords - sourceCoord, sourceCoord - minCoords);
            int maxLayer = math.max(highestCoordDistances.x,
                math.max(highestCoordDistances.y, highestCoordDistances.z));

            for (int l = 0; l <= maxLayer; l++)
            {
                int2 yRange = new int2(sourceCoord.y - l, sourceCoord.y + l);
                int2 zRange = new int2(sourceCoord.z - l, sourceCoord.z + l);
                int2 xRange = new int2(sourceCoord.x - l, sourceCoord.x + l);

                for (int y = yRange.x; y <= yRange.y; y++)
                {
                    int yDistToEdge = math.min(y - minCoords.y, maxCoords.y - y);

                    if (yDistToEdge < 0)
                    {
                        continue;
                    }

                    for (int z = zRange.x; z <= zRange.y; z++)
                    {
                        int zDistToEdge = math.min(z - minCoords.z, maxCoords.z - z);

                        if (zDistToEdge < 0)
                        {
                            continue;
                        }

                        for (int x = xRange.x; x <= xRange.y; x++)
                        {
                            int xDistToEdge = math.min(x - minCoords.x, maxCoords.x - x);

                            if (xDistToEdge < 0)
                            {
                                continue;
                            }

                            int3 coords = new int3(x, y, z);
                            int3 coordDistToCenter = math.abs(coords - sourceCoord);
                            int maxCoordsDist = math.max(coordDistToCenter.x,
                                math.max(coordDistToCenter.y, coordDistToCenter.z));

                            if (maxCoordsDist != l)
                            {
                                x = xRange.y - 1;
                                continue;
                            }

                            int cellIndex = UniformOriginGrid.GetCellIndexFromCoords(in grid, coords);
                            SpatialDatabaseCell cell = cellsBuffer[cellIndex];
                            collector.OnVisitCell(in cell, in elementsBuffer,
                                out bool shouldEarlyExit);
                            if (shouldEarlyExit)
                            {
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
}