using UnityEngine;
using System.Collections.Generic;

public class FuzzyDDAController : MonoBehaviour
{
    [Header("References")]
    public GameManager gameManager;
    public TerrainManager terrainManager;
    public PlayerController player;

    [Header("Update")]
    [Tooltip("Seconds between DDA evaluations")]
    public float updateInterval = 5f;

    [Header("Sliding Window")]
    [Tooltip("Duration W in seconds used by NMR and CR calculations")]
    public float windowDuration = 5f;

    [Header("EMA Smoothing")]
    [Range(0f, 1f)]
    [Tooltip("Ground skill smoothing factor alpha")]
    public float alpha = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("Aerial skill smoothing factor beta")]
    public float beta = 0.5f;

    [Header("Normalisation")]
    [Tooltip("The highest observed backflips/sec from playtesting.")]
    public float crCap = 0.2f;
    [Tooltip("Session length at which Tp saturates to 1")]
    public float sessionMax = 120f;

    [Header("Ground Skill Sensitivity")]
    [Tooltip("Maximum number of obstacles counted")]
    [Min(1)]
    public int sensitivityCap = 2;

    [Header("Speed")]
    [Tooltip("Minimum scroll speed")]
    public float minSpeed = 10f;
    [Tooltip("Maximum safe speed")]
    public float maxSpeed = 20f;

    [Header("Obstacle Density")]
    public float minSpawnChance = 0.05f;
    public float maxSpawnChance = 0.5f;

    [Header("Speed Crisp Values  [DL, DS, NC, IS, IL]")]
    public float[] speedCrispValues = { -4f, -2f, 0f, 1f, 2f };

    [Header("Obstacle Density Crisp Values  [DL, DS, NC, IS, IL]")]
    public float[] densityCrispValues = { -0.6f, -0.3f, 0f, 0.05f, 0.1f };

    [Header("Slide Density Crisp Values  [DL, DS, NC, IS, IL]")]
    public float[] slideCrispValues = { -0.6f, -0.3f, 0f, 0.15f, 0.3f };

    [Header("Slide Spawn Weights")]
    [Tooltip("SmallSlide weight")]
    public Vector2 smallSlideWeightRange = new Vector2(6f, 20f);
    [Tooltip("MediumSlide weight")]
    public Vector2 medSlideWeightRange = new Vector2(3f, 15f);

    [Tooltip("BigSlide weight")]
    public Vector2 bigSlideWeightRange = new Vector2(1.5f, 5f);

    [Header("Cheat")]
    public bool overrideGroundSkill = false;
    [Range(0f, 1f)] public float fixedSg = 0f;
    public bool overrideAerialSkill = false;
    [Range(0f, 1f)] public float fixedSa = 0f;
    public bool overrideTimePeriod = false;
    [Range(0f, 1f)] public float fixedTp = 0f;


    private const float K_COL = 1.0f;
    private const float K_NM = 0.8f;

    private float Sg = 0.5f;
    private float Sa = 0.5f;

    private float currentSpeed;
    private float slideDensityScore = 0.5f;
    private float obstacleDensityScore = 0.5f;

    private struct WeightedEvent { public float time; public float weight; }
    private List<WeightedEvent> nearMissBuffer = new List<WeightedEvent>();
    private List<float> backflipBuffer = new List<float>();

    // used to compute the dynamic nmrMax each cycle
    private List<float> obstacleSpawnBuffer = new List<float>();
    private float nmrMax;

    private float timeSinceLastUpdate = 0f;

    private float lastDeltaV = 0f;
    private float lastDeltaDensity = 0f;
    private float lastDeltaSlide = 0f;

    private float SessionTime => gameManager.GetSessionTime();


    //Fuzzy Table
    //columns: [deltaV_label, deltaP_label, deltaSlide_label]
    //0=DL, 1=DS, 2=NC, 3=IS, 4=IL
    private static readonly int[,] RuleTable = new int[27, 3]
    {                //     (g,a,T)
        { 0, 0, 0 }, // R1  (L,L,L)
        { 1, 0, 0 }, // R2  (L,L,M)
        { 1, 1, 1 }, // R3  (L,L,H)
        { 0, 0, 1 }, // R4  (L,M,L)
        { 1, 0, 1 }, // R5  (L,M,M)
        { 2, 1, 2 }, // R6  (L,M,H)
        { 1, 0, 1 }, // R7  (L,H,L)
        { 2, 1, 2 }, // R8  (L,H,M)
        { 3, 1, 3 }, // R9  (L,H,H)
        { 0, 1, 0 }, // R10 (M,L,L)
        { 1, 1, 0 }, // R11 (M,L,M)
        { 2, 2, 1 }, // R12 (M,L,H)
        { 2, 2, 1 }, // R13 (M,M,L)
        { 3, 2, 2 }, // R14 (M,M,M)
        { 4, 2, 2 }, // R15 (M,M,H)
        { 2, 2, 3 }, // R16 (M,H,L)
        { 3, 2, 4 }, // R17 (M,H,M)
        { 4, 3, 4 }, // R18 (M,H,H)
        { 1, 1, 0 }, // R19 (H,L,L)
        { 2, 2, 1 }, // R20 (H,L,M)
        { 3, 3, 1 }, // R21 (H,L,H)
        { 3, 3, 2 }, // R22 (H,M,L)
        { 3, 3, 3 }, // R23 (H,M,M)
        { 4, 3, 3 }, // R24 (H,M,H)
        { 3, 3, 3 }, // R25 (H,H,L)
        { 4, 3, 3 }, // R26 (H,H,M)
        { 4, 4, 4 }, // R27 (H,H,H)
    };

    void Start()
    {
        if (player != null)
        {
            player.OnBackflipCompleted += HandleBackflip;
            player.OnNearMiss += HandleNearMiss;
            player.OnCollision += HandleCollision;
        }
        else
        {
            Debug.LogWarning("PlayerController reference is null");
        }

        if (gameManager != null)
        {
            currentSpeed = gameManager.CurrentSpeed;
        }
        else
        {
            currentSpeed = minSpeed;
        }

        if (terrainManager != null)
        {
            obstacleDensityScore = Mathf.InverseLerp(minSpawnChance, maxSpawnChance, terrainManager.spawnChance);
            slideDensityScore = Mathf.InverseLerp(smallSlideWeightRange.x, smallSlideWeightRange.y, terrainManager.weightSmallSlide);
        }

        //cheat
        if (overrideGroundSkill)
        {
            Sg = fixedSg;
        }
        if (overrideAerialSkill)
        {
            Sa = fixedSa;
        }
    }

    void OnDestroy()
    {
        if (player != null)
        {
            player.OnBackflipCompleted -= HandleBackflip;
            player.OnNearMiss -= HandleNearMiss;
            player.OnCollision -= HandleCollision;
        }
    }

    void Update()
    {
        if (gameManager == null || !gameManager.IsRunning)
        {
            return;
        }

        timeSinceLastUpdate += Time.deltaTime;
        if (timeSinceLastUpdate >= updateInterval)
        {
            timeSinceLastUpdate = 0f;
            RunDDACycle();
        }
    }


    private void HandleBackflip()
    {
        backflipBuffer.Add(SessionTime);
    }
    private void HandleNearMiss(float dist)
    {
        nearMissBuffer.Add(new WeightedEvent { time = SessionTime, weight = K_NM });
    }
    private void HandleCollision()
    {
        nearMissBuffer.Add(new WeightedEvent { time = SessionTime, weight = K_COL });
    }


    //used to calculate nmrMax 
    public void NotifyObstacleSpawned()
    {
        obstacleSpawnBuffer.Add(SessionTime);
    }


    private void RunDDACycle()
    {
        float t = SessionTime;

        float sg = ComputeGroundSkill(t);
        float sa = ComputeAerialSkill(t);
        float tp;
        if (overrideTimePeriod)
        {
            tp = fixedTp;
        }
        else
        {
            tp = ComputePlayDuration(t);
        }

        FuzzyTriplet muSg = Fuzzify(sg);
        FuzzyTriplet muSa = Fuzzify(sa);
        FuzzyTriplet muTp = Fuzzify(tp);

        float[] firingStrengths = ComputeFiringStrengths(muSg, muSa, muTp);

        Defuzzify(firingStrengths,
                  out float deltaV,
                  out float deltaDensity,
                  out float deltaSlide);

        lastDeltaV = deltaV;
        lastDeltaDensity = deltaDensity;
        lastDeltaSlide = deltaSlide;

        ApplyOutputsToGame(deltaV, deltaDensity, deltaSlide);
    }


    private float ComputeGroundSkill(float currentTime)
    {
        if (overrideGroundSkill)
        {
            Sg = fixedSg;
            return Sg;
        }
        //remove all near miss in the buffer
        for (int i = nearMissBuffer.Count - 1; i >= 0; i--)
        {
            WeightedEvent nearmiss = nearMissBuffer[i];

            if (currentTime - nearmiss.time > windowDuration)
            {
                nearMissBuffer.RemoveAt(i);
            }
        }
        //remove all obstacle count in the buffer
        for (int i = obstacleSpawnBuffer.Count - 1; i >= 0; i--)
        {
            float spawnTime = obstacleSpawnBuffer[i];
            if (currentTime - spawnTime > windowDuration)
            {
                obstacleSpawnBuffer.RemoveAt(i);
            }
        }
        int obstaclesInWindow = obstacleSpawnBuffer.Count;

        float nmrHat;
        if (obstaclesInWindow == 0)
        {
            nmrHat = 0f;
        }
        else
        {
            int effectiveObstacles = Mathf.Min(obstaclesInWindow, sensitivityCap);
            nmrMax = (effectiveObstacles * K_COL) / windowDuration;

            float weightSum = 0f;
            foreach (var nearmiss in nearMissBuffer)
            {
                weightSum += nearmiss.weight;
            }

            nmrHat = Mathf.Clamp01((weightSum / windowDuration) / nmrMax);
        }

        Sg = alpha * Sg + (1f - alpha) * (1f - nmrHat);

        return Sg;
    }

    private float ComputeAerialSkill(float currentTime)
    {
        if (overrideAerialSkill)
        {
            Sa = fixedSa;
            return Sa;
        }
        //remove all backflips in the buffer
        for (int i = backflipBuffer.Count - 1; i >= 0; i--)
        {
            float stamp = backflipBuffer[i];

            if (currentTime - stamp > windowDuration)
            {
                backflipBuffer.RemoveAt(i);
            }
        }

        float crHat = Mathf.Clamp01((backflipBuffer.Count / windowDuration) / crCap);
        Sa = beta * Sa + (1f - beta) * crHat;

        return Sa;
    }

    private float ComputePlayDuration(float currentTime)
    {
        return Mathf.Clamp01(currentTime / sessionMax);
    }


    //fuzzification
    private struct FuzzyTriplet { public float low, med, high; }

    //from the formulas in proposal
    private FuzzyTriplet Fuzzify(float x) => new FuzzyTriplet
    {
        low = Mathf.Max(0f, (0.5f - x) / 0.5f),
        med = Mathf.Max(0f, Mathf.Min(x / 0.5f, (1f - x) / 0.5f)),
        high = Mathf.Max(0f, (x - 0.5f) / 0.5f)
    };


    private float[] ComputeFiringStrengths(FuzzyTriplet muSg, FuzzyTriplet muSa, FuzzyTriplet muTp)
    {
        float[] sg = { muSg.low, muSg.med, muSg.high };
        float[] sa = { muSa.low, muSa.med, muSa.high };
        float[] tp = { muTp.low, muTp.med, muTp.high };

        float[] firingStrengthOfAllCombination = new float[27];
        int indexCounter = 0;
        for (int groundIndex = 0; groundIndex < 3; groundIndex++)
        {
            for (int airIndex = 0; airIndex < 3; airIndex++)
            {
                for (int timeIndex = 0; timeIndex < 3; timeIndex++)
                {
                    firingStrengthOfAllCombination[indexCounter++] = Mathf.Min(sg[groundIndex], Mathf.Min(sa[airIndex], tp[timeIndex]));
                }
            }
        }
        return firingStrengthOfAllCombination;
    }


    //defuzzify
    private void Defuzzify(float[] firingStrengths, out float deltaV, out float deltaDensity, out float deltaSlide)
    {
        float totalWeight = 0f, totaldeltaV = 0f, totaldeltaObstacle = 0f, totaldeltaSlide = 0f;

        for (int i = 0; i < 27; i++)
        {
            float firingStrength = firingStrengths[i];
            if (firingStrength < 1e-7f) //check if value is very small
            {
                continue; //then just skips it
            }

            totalWeight += firingStrength;
            //translate the fuzzy into a real number using crisp values
            totaldeltaV += firingStrength * speedCrispValues[RuleTable[i, 0]];
            totaldeltaObstacle += firingStrength * densityCrispValues[RuleTable[i, 1]];
            totaldeltaSlide += firingStrength * slideCrispValues[RuleTable[i, 2]];
        }

        if (totalWeight < 1e-6f) //check if value is very small or equal to 0
        {
            deltaV = deltaDensity = deltaSlide = 0f; //then set everything to 0
            return;
        }

        //centroid
        deltaV = totaldeltaV / totalWeight;
        deltaDensity = totaldeltaObstacle / totalWeight;
        deltaSlide = totaldeltaSlide / totalWeight;
    }


    private void ApplyOutputsToGame(float deltaV, float deltaDensity, float deltaSlide)
    {
        //set speed
        currentSpeed = Mathf.Clamp(currentSpeed + deltaV, minSpeed, maxSpeed);
        gameManager?.SetSpeed(currentSpeed);

        //set obstacle density
        obstacleDensityScore = Mathf.Clamp01(obstacleDensityScore + deltaDensity);
        float newSpawnChance = Mathf.Lerp(minSpawnChance, maxSpawnChance, obstacleDensityScore);
        terrainManager.SetObstacleDensity(newSpawnChance);

        //set slide density
        slideDensityScore = Mathf.Clamp01(slideDensityScore + deltaSlide);
        float t = slideDensityScore;
        float smallW = Mathf.Lerp(smallSlideWeightRange.x, smallSlideWeightRange.y, t);
        float medW = Mathf.Lerp(medSlideWeightRange.x, medSlideWeightRange.y, t);
        float bigW = Mathf.Lerp(bigSlideWeightRange.x, bigSlideWeightRange.y, t);
        terrainManager.SetSlideWeights(smallW, medW, bigW);
    }



    public float GetGroundSkill()
    {
        return Sg;
    }
    public float GetAerialSkill()
    {
        return Sa;
    }
    public float GetSlideDensityScore()
    {
        return slideDensityScore;
    }
    public float GetObstacleDensityScore()
    {
        return obstacleDensityScore;
    }
    public float GetCurrentSpeed()
    {
        return currentSpeed;
    }
    public int GetNearMissCount()
    {
        return nearMissBuffer.Count;
    }
    public int GetBackflipCount()
    {
        return backflipBuffer.Count;
    }
    public float GetLastDeltaV()
    {
        return lastDeltaV;
    }
    public float GetLastDeltaDensity()
    {
        return lastDeltaDensity;
    }
    public float GetLastDeltaSlide()
    {
        return lastDeltaSlide;
    }
}