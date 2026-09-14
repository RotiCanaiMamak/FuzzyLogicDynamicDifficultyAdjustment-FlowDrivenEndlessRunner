using UnityEngine;
using System.Collections.Generic;

public class TerrainManager : MonoBehaviour
{
    [Header("References")]
    public TerrainChunk chunkPrefab;
    public Transform player;
    public FuzzyDDAController ddaController;

    [Header("Chunk Settings")]
    public int visibleChunksAhead = 10;
    public float chunkWidth = 10f;
    [Min(1)]
    public int chunksBehindPlayer = 2;

    [Header("Floating Origin")]
    [Tooltip("World shifts back to zero once player crosses this distance (to avoid jitter)")]
    public float originShiftThreshold = 1000f;

    [Header("Depth")]
    [Tooltip("Minimum underground fill depth for any chunk.")]
    public float baseDepth = 20f;
    [Tooltip("Prevents the SpriteShape polygon from self intersecting on large drops.")]
    public float depthSafetyMargin = 10f;

    [Header("Terrain Weights")]
    public float weightSlopeDown = 30f; //to make terrain curves a bit to look more natural
    public float weightSlopeUp = 5f;
    public float weightSmallSlide = 15f;
    public float weightMediumSlide = 10f;
    public float weightBigSlide = 5f;

    [Header("Slope Settings")]
    public float minDownSlope = 0f;
    public float maxDownSlope = 1f;
    public float minUpSlope = 0f;
    public float maxUpSlope = 1f;

    [Header("Small Slide  (1-chunk sequence)")]
    public float smallSlideDropMin = 2.5f;
    public float smallSlideDropMax = 2.5f;

    [Header("Medium Slide  (2-chunk sequence)")]
    public float medSlideDropPerChunkMin = 5f;
    public float medSlideDropPerChunkMax = 5f;

    [Header("Big Slide  (3-chunk sequence)")]
    public float bigSlideDropPerChunkMin = 10f;
    public float bigSlideDropPerChunkMax = 10f;

    [Header("Recovery Phase")]
    [Tooltip("Number of guaranteed normal chunks (not slide) after a MediumSlide or BigSlide finished")]
    [SerializeField] private int postSlideFlatChunks = 6;
    [SerializeField] private float recoverySlopeDownWeight = 20f;
    [SerializeField] private float recoverySlopeUpWeight = 5f;

    [Header("Obstacle Spawning")]
    public GameObject obstaclePrefab;

    [Range(0f, 1f)]
    public float spawnChance = 0.05f;
    public int candidatesPerChunk = 1; //only 1 obstacle allowed on 1 chunk

    public Vector3 minObstacleScale = new Vector3(1f, 1f, 1f);
    public Vector3 maxObstacleScale = new Vector3(1f, 1f, 1f);

    //for spawning obstacle (only spawn at eligible terrain)
    private static readonly HashSet<TerrainType> _obstacleEligible =
        new HashSet<TerrainType>
        {
            TerrainType.Flat,
            TerrainType.SlopeDown,
            TerrainType.SlopeUp,
            TerrainType.SmallSlide,
        };

    //for terrain types that needs specific amount of chunks (medium slide, big slide)
    private int chunksRemainingInSequence = 0;
    private TerrainType currentSequenceType = TerrainType.Flat;

    //for calcualating recovery after slides
    private int forcedFlatCount = 0;

    private Queue<TerrainChunk> activeChunks = new Queue<TerrainChunk>();
    private float nextSpawnX = 0f;
    private float currentEndY = 0f;
    private Vector3 prevSlopeVector = Vector3.right;

    private enum TerrainType { Flat, SlopeDown, SlopeUp, SmallSlide, MediumSlide, BigSlide }

    void Start()
    {
        currentEndY = 0f;
        for (int i = 0; i < visibleChunksAhead; i++)
        {
            SpawnChunk(TerrainType.Flat);
        }
    }

    void Update()
    {
        if (player == null)
        {
            return;
        }

        //keep all world coordinates close to zero to avoid floating point precision loss at large distances
        if (Mathf.Abs(player.position.x) > originShiftThreshold || Mathf.Abs(player.position.y) > originShiftThreshold)
        {
            ShiftWorld();
        }

        //spawn ahead of the player
        float spawnThreshold = player.position.x + visibleChunksAhead * chunkWidth;
        while (nextSpawnX < spawnThreshold)
        {
            if (chunksRemainingInSequence > 0)
            {
                SpawnChunk(currentSequenceType);
                chunksRemainingInSequence--;

                //for recovery phase
                if (chunksRemainingInSequence == 0 && (currentSequenceType == TerrainType.MediumSlide || currentSequenceType == TerrainType.BigSlide))
                {
                    forcedFlatCount = postSlideFlatChunks;
                }
            }
            else if (forcedFlatCount > 0)
            {
                SpawnChunk(PickRecoveryType());
                forcedFlatCount--;
            }
            else
            {
                TerrainType rolledType = PickTerrainType();

                switch (rolledType)
                {
                    case TerrainType.SmallSlide:
                        currentSequenceType = TerrainType.SmallSlide;
                        chunksRemainingInSequence = 1;
                        break;
                    case TerrainType.MediumSlide:
                        currentSequenceType = TerrainType.MediumSlide;
                        chunksRemainingInSequence = 2;
                        break;
                    case TerrainType.BigSlide:
                        currentSequenceType = TerrainType.BigSlide;
                        chunksRemainingInSequence = 3;
                        break;
                }

                if (chunksRemainingInSequence > 0)
                {
                    SpawnChunk(currentSequenceType);
                    chunksRemainingInSequence--;
                }
                else
                {
                    SpawnChunk(rolledType);
                }
            }
        }

        //keep chunks alive
        float chunkAliveThreshold = player.position.x - (chunksBehindPlayer - 1) * chunkWidth;
        while (activeChunks.Count > 0 && activeChunks.Peek().transform.position.x + chunkWidth < chunkAliveThreshold)
        {
            Destroy(activeChunks.Dequeue().gameObject);
        }
    }

    //called when the world position is over the threshold
    void ShiftWorld()
    {
        Vector3 shift = new Vector3(player.position.x, player.position.y, 0f);

        player.position -= shift;

        foreach (TerrainChunk chunk in activeChunks)
        {
            chunk.transform.position -= shift;
            chunk.RefreshAfterShift();
        }

        CameraFollow cam = Camera.main.GetComponent<CameraFollow>();
        cam.OnWorldShift(shift);

        nextSpawnX -= shift.x;
        currentEndY -= shift.y;
    }

    void SpawnChunk(TerrainType type)
    {
        TerrainChunk chunk = Instantiate(chunkPrefab, transform);

        chunk.transform.position = new Vector3(nextSpawnX, currentEndY, 0f);
        chunk.chunkWidth = chunkWidth;
        chunk.startY = currentEndY;

        float verticalDrop = 0f;

        switch (type)
        {
            case TerrainType.Flat:
                chunk.cliffHeight = 0f;
                chunk.endY = currentEndY;
                verticalDrop = 0f;
                break;

            case TerrainType.SlopeDown:
                chunk.cliffHeight = 0f;
                chunk.endY = currentEndY - Random.Range(minDownSlope, maxDownSlope);
                verticalDrop = currentEndY - chunk.endY;
                break;

            case TerrainType.SlopeUp:
                chunk.cliffHeight = 0f;
                chunk.endY = currentEndY + Random.Range(minUpSlope, maxUpSlope);
                verticalDrop = 0f;
                break;

            case TerrainType.SmallSlide:
                chunk.cliffHeight = 0f;
                chunk.endY = currentEndY - Random.Range(smallSlideDropMin, smallSlideDropMax);
                verticalDrop = currentEndY - chunk.endY;
                break;

            case TerrainType.MediumSlide:
                chunk.cliffHeight = 0f;
                chunk.endY = currentEndY - Random.Range(medSlideDropPerChunkMin, medSlideDropPerChunkMax);
                verticalDrop = currentEndY - chunk.endY;
                break;

            case TerrainType.BigSlide:
                chunk.cliffHeight = 0f;
                chunk.endY = currentEndY - Random.Range(bigSlideDropPerChunkMin, bigSlideDropPerChunkMax);
                verticalDrop = currentEndY - chunk.endY;
                break;
        }

        chunk.depth = Mathf.Max(baseDepth, verticalDrop + depthSafetyMargin);

        Vector3 slopeVector = new Vector3(chunkWidth, chunk.endY - chunk.startY, 0f).normalized;

        chunk.Build(prevSlopeVector, slopeVector);
        prevSlopeVector = slopeVector;

        TrySpawnObstacles(chunk, type);

        currentEndY = chunk.RightEdgeY;
        nextSpawnX += chunkWidth;
        activeChunks.Enqueue(chunk);
    }

    private void TrySpawnObstacles(TerrainChunk chunk, TerrainType type)
    {
        if (obstaclePrefab == null)
        {
            return;
        }
        if (!_obstacleEligible.Contains(type))
        {
            return;
        }
        if (candidatesPerChunk <= 0)
        {
            return;
        }

        int spawnCount = Mathf.Min(PoissonSample(spawnChance), candidatesPerChunk);

        for (int i = 0; i < spawnCount; i++)
        {
            float intervalWidth = 1f / spawnCount;  // spread the N obstacles evenly
            float middleOftheChunks = (i + 0.5f) * intervalWidth;
            float jitterRange = intervalWidth * 0.2f;
            float t = Mathf.Clamp(middleOftheChunks + Random.Range(-jitterRange, jitterRange), 0.05f, 0.95f);

            if (!chunk.EvaluateSurface(t, out Vector3 worldPos, out Vector3 worldNormal))
            {
                continue;
            }

            float surfaceAngle = Mathf.Atan2(worldNormal.x, worldNormal.y) * Mathf.Rad2Deg * -1f;
            Quaternion rotate = Quaternion.Euler(0f, 0f, surfaceAngle);

            GameObject obs = Instantiate(obstaclePrefab, worldPos, rotate, chunk.transform);
            obs.tag = "Obstacle";
            ddaController.NotifyObstacleSpawned();

            obs.transform.localScale = new Vector3(
                Random.Range(minObstacleScale.x, maxObstacleScale.x),
                Random.Range(minObstacleScale.y, maxObstacleScale.y),
                Random.Range(minObstacleScale.z, maxObstacleScale.z)
            );
        }
    }

    private int PoissonSample(float lambda)
    {
        //Knuth algorithm: accurate for small lambda (< ~30)
        float L = Mathf.Exp(-lambda);
        int k = 0;
        float p = 1f;
        do
        {
            k++;
            p *= Random.value;
        } while (p > L);

        return k - 1;
    }

    //used after slides
    TerrainType PickRecoveryType()
    {
        float roll = Random.Range(0f, 100f);

        if ((roll -= recoverySlopeDownWeight) < 0f)
        {
            return TerrainType.SlopeDown;
        }
        if ((roll -= recoverySlopeUpWeight) < 0f)
        {
            return TerrainType.SlopeUp;
        }
        return TerrainType.Flat;
    }


    TerrainType PickTerrainType()
    {
        float weightFlat = Mathf.Max(0f, 100f - weightSlopeDown - weightSlopeUp - weightSmallSlide - weightMediumSlide - weightBigSlide);

        float totalWeight = weightSlopeDown + weightSlopeUp + weightSmallSlide + weightMediumSlide + weightBigSlide + weightFlat;
        float roll = Random.Range(0f, totalWeight);

        if ((roll -= weightSlopeUp) < 0f)
        {
            return TerrainType.SlopeUp;
        }
        if ((roll -= weightSmallSlide) < 0f)
        {
            return TerrainType.SmallSlide;
        }
        if ((roll -= weightMediumSlide) < 0f)
        {
            return TerrainType.MediumSlide;
        }
        if ((roll -= weightBigSlide) < 0f)
        {
            return TerrainType.BigSlide;
        }
        if ((roll -= weightSlopeDown) < 0f)
        {
            return TerrainType.SlopeDown;
        }
        return TerrainType.Flat;
    }


    public void SetSlideWeights(float smallW, float medW, float bigW)
    {
        weightSmallSlide = smallW;
        weightMediumSlide = medW;
        weightBigSlide = bigW;
    }
    public void SetObstacleDensity(float newSpawnChance)
    {
        spawnChance = newSpawnChance;
    }
    public float GetSpawnChance()
    {
        return spawnChance;
    }
    public float GetTotalSlideWeight()
    {
        return (weightSmallSlide + weightMediumSlide + weightBigSlide) / 100f;
    }
    public float GetSmallSlideWeight()
    {
        return weightSmallSlide / 100f;
    }
    public float GetMedSlideWeight()
    {
        return weightMediumSlide / 100f;
    }
    public float GetBigSlideWeight()
    {
        return weightBigSlide / 100f;
    }
}