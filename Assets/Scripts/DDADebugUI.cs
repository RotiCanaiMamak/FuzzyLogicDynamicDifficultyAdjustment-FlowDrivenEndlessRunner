using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class DDADebugUI : MonoBehaviour
{
    [Header("DDA Controller)")]
    [SerializeField] private FuzzyDDAController ddaController;

    [Header("DDA Panel")]
    [Tooltip("DDAPanel (will change width on minimized)")]
    [SerializeField] private RectTransform ddaPanel;
    [SerializeField] private Image panelBackground;
    [Tooltip("Inside DDAPanel (will disappear when minimized)")]
    [SerializeField] private GameObject contentArea;
    [SerializeField] private Button toggleButton;
    [SerializeField] private TMP_Text toggleButtonLabel; 

    [Header("Restart")]
    [Tooltip("Restart Button (will reset scene when pressed)")]
    [SerializeField] private Button restartButton;

    [Header("Graphs")]
    [Tooltip("Current Speed")]
    [SerializeField] private RawImage graphSpeed;
    [SerializeField] private TMP_Text readoutSpeed;
    [Tooltip("Obstacle Spawn Chance")]
    [SerializeField] private RawImage graphSpawnChance;
    [SerializeField] private TMP_Text readoutSpawnChance;
    [Tooltip("Total Slide Spawn Chance")]
    [SerializeField] private RawImage graphSlideChance;
    [SerializeField] private TMP_Text readoutSlideChance;
    [Tooltip("Ground Skill Sg")]
    [SerializeField] private RawImage graphSg;
    [SerializeField] private TMP_Text readoutSg;
    [Tooltip("Aerial Skill Sa")]
    [SerializeField] private RawImage graphSa;
    [SerializeField] private TMP_Text readoutSa;
    [Header("Graph Sampling")]
    [Tooltip("Seconds between data points pushed to each graph.")]
    [SerializeField] private float sampleInterval = 0.1f;

    [Header("Slider")]
    [SerializeField] private RectTransform sliderContent;

    [Header("Events")]
    [Tooltip("Near Miss Count in Buffer")]
    [SerializeField] private TMP_Text readoutNearMiss;
    [Tooltip("Backflip Count in Buffer")]
    [SerializeField] private TMP_Text readoutBackflip;
    [Tooltip("Show DDA Output (Fuzzy Fire)")]
    [SerializeField] private TMP_Text readoutFuzzyFiring;


    private static readonly Color ColSpeed = new Color(1.00f, 0.75f, 0.20f);  //amber
    private static readonly Color ColSpawnChance = new Color(1.00f, 0.42f, 0.42f);  //red
    private static readonly Color ColSlideChance = new Color(0.85f, 0.45f, 1.00f);  //purple
    private static readonly Color ColSlideChanceMed = new Color(0.68f, 0.58f, 1.00f);  //lavender
    private static readonly Color ColSlideChanceDark = new Color(0.52f, 0.18f, 0.72f);  //violet 
    private static readonly Color ColSg = new Color(0.33f, 0.85f, 0.45f);  //green
    private static readonly Color ColSa = new Color(0.40f, 0.72f, 1.00f);  //blue

    private struct SliderDesc
    {
        public string label; //slider name
        public float min; 
        public float max;
        public Func<float> getter; //used to get value from game
        public Action<float> setter; //used to set value to DDA
    }

    //graphs
    private GraphRenderer _grSpeed; 
    private GraphRenderer _grSpawnChance;
    private GraphRenderer _grSlideChance;
    private GraphRenderer _grSg;
    private GraphRenderer _grSa;

    private readonly List<Slider> _sliders = new List<Slider>(); //stores references to the actual interactive Unity UI Slider components (for reading mouse input and move slider)
    private readonly List<TMP_Text> _valueLabels = new List<TMP_Text>(); //store the text objects of the value of the sliders (to update the value when moving slider)
    private readonly List<SliderDesc> _descriptors = new List<SliderDesc>(); //store the struct to tell UI how many sliders to build

    private bool _isMinimised = false;
    private float _sampleTimer = 0f; //for graph sampling


    private void Start()
    {
        if (ddaController == null)
        {
            ddaController = FindFirstObjectByType<FuzzyDDAController>();
            if (ddaController == null)
            {
                Debug.LogWarning("FuzzyDDAController not found, graphs will show zero and sliders are disabled.");
            }
        }

        //initialize 5 graphs
        _grSpeed = InitGraph(graphSpeed, ColSpeed, "Speed");
        _grSpawnChance = InitGraph(graphSpawnChance, ColSpawnChance, "SpawnChance");
        _grSlideChance = InitGraph(graphSlideChance, new Color[] { ColSlideChance, ColSlideChanceMed, ColSlideChanceDark }, "SlideChance");  
        _grSg = InitGraph(graphSg, ColSg, "Sg");
        _grSa = InitGraph(graphSa, ColSa, "Sa");
        //display graph labels and value
        TintLabel(readoutSpeed, ColSpeed);
        TintLabel(readoutSpawnChance, ColSpawnChance);
        TintLabel(readoutSlideChance, ColSlideChance);
        TintLabel(readoutSg, ColSg);
        TintLabel(readoutSa, ColSa);

        //enable word wrapping on the graph labels
        TMP_Text[] graphLabels = new TMP_Text[] { readoutSpeed, readoutSpawnChance, readoutSlideChance, readoutSg, readoutSa };
        foreach (var label in graphLabels)
        {
            if (label != null)
            {
                label.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        //sliders
        BuildSliderDescriptors();
        BuildSliderUI();

        //toggleButton
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleMinimise);
        }

        //restartButton
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        SetMinimised(true);
    }

    private void Update()
    {
        //graphs
        float speed = 0f;
        float spawn = 0f;
        float smallSlideW = 0f;
        float medSlideW = 0f;
        float bigSlideW = 0f;
        float sg = 0f;
        float sa = 0f;
        if (ddaController != null)
        {
            speed = ddaController.GetCurrentSpeed();
            sg = ddaController.GetGroundSkill();
            sa = ddaController.GetAerialSkill();
      
            if (ddaController.terrainManager != null)
            {
                spawn = ddaController.terrainManager.GetSpawnChance();

                //convert weights to 0~1 range
                smallSlideW = ddaController.terrainManager.weightSmallSlide / 100f;
                medSlideW = ddaController.terrainManager.weightMediumSlide / 100f;
                bigSlideW = ddaController.terrainManager.weightBigSlide / 100f;
            }
        }
        float totalSlideW = smallSlideW + medSlideW + bigSlideW;

        //readout labels and values
        SetLabel(readoutSpeed, $"Speed: {speed:F2}");
        SetLabel(readoutSpawnChance, $"Spawn: {spawn:P1}");
        SetLabel(readoutSlideChance, $"Slides: {totalSlideW:P0} (S:{smallSlideW:P0} M:{medSlideW:P0} B:{bigSlideW:P0})");
        SetLabel(readoutSg, $"Sg: {sg:F2}");
        SetLabel(readoutSa, $"Sa: {sa:F2}");

        //show events' count in buffer
        if (ddaController != null)
        {
            //near misses and backflips in buffer
            SetLabel(readoutNearMiss, $"Near Misses: {ddaController.GetNearMissCount()}");
            SetLabel(readoutBackflip, $"Backflips: {ddaController.GetBackflipCount()}");

            //fuzzy firing
            string labelSpeed = FuzzyLabel(ddaController.GetLastDeltaV(), ddaController.speedCrispValues);
            string labelObstacle = FuzzyLabel(ddaController.GetLastDeltaDensity(), ddaController.densityCrispValues);
            string labelSlide = FuzzyLabel(ddaController.GetLastDeltaSlide(), ddaController.slideCrispValues);
            SetLabel(readoutFuzzyFiring, $"Speed: {labelSpeed}\nObstacles: {labelObstacle}\nSlide: {labelSlide}");
        }
        else
        {
            SetLabel(readoutNearMiss, $"Near Misses: NULL");
            SetLabel(readoutBackflip, $"Backflips: NULL");

            SetLabel(readoutFuzzyFiring, "Speed: NULL  Obstacle: NULL  Slide: NULL");
        }

        //push graph samples
        _sampleTimer += Time.deltaTime; 
        if (_sampleTimer >= sampleInterval) //check for timer interval
        {
            _sampleTimer = 0f; //reset timer

            float speedNorm = 0f;
            if (ddaController != null)
            {
                //normalize to ensure line on graph stay in graph box (to 0~1)
                speedNorm = Mathf.InverseLerp(ddaController.minSpeed, ddaController.maxSpeed, speed); 
            }
            else
            {
                speedNorm = 0f;
            }
            PushSample(_grSpeed, speedNorm);
            PushSample(_grSpawnChance, spawn);
            PushSlidesSample(_grSlideChance, smallSlideW, medSlideW, bigSlideW);
            PushSample(_grSg, sg);
            PushSample(_grSa, sa);
        }

        SyncSliderValueLabels(); //ensure slider changes even when value changes automatically
    }



    //ensures the graph is rendered on the RawImage's GameObject
    private static GraphRenderer InitGraph(RawImage rawImage, Color lineColor, string debugTag)
    {
        if (rawImage == null)
        {
            Debug.LogWarning($"RawImage for graph '{debugTag}' is not assigned. Graph is disabled.");
            return null;
        }
        return InitGraph(rawImage, new Color[] { lineColor }, debugTag);
    }
    //for slides graph specifically (multiple line colours)
    private static GraphRenderer InitGraph(RawImage rawImage, Color[] lineColors, string debugTag)
    {
        if (rawImage == null)
        {
            Debug.LogWarning($"RawImage for graph '{debugTag}' is not assigned. Graph is disabled.");
            return null;
        }
        GraphRenderer gr = rawImage.gameObject.GetComponent<GraphRenderer>();
        for (int i = 0; i < lineColors.Length; i++)
        {
            gr.SetSeriesColor(i, lineColors[i]); //tells GraphRenderer each series' specific colour
        }

        return gr;
    }

    //push sample to the graph
    private static void PushSample(GraphRenderer gr, float value)
    {
        if (gr == null)
        {
            return;
        }
        gr.AddSample(0, value);
        gr.AdvanceColumn();
        gr.Flush();
    }
    //push 3 sample to the graph (specifcally for slide's graph)
    private static void PushSlidesSample(GraphRenderer gr, float small, float med, float big)
    {
        if (gr == null)
        {
            return;
        }
        gr.AddSample(0, small);
        gr.AddSample(1, med);
        gr.AddSample(2, big);
        gr.AdvanceColumn();
        gr.Flush();
    }


    private void ToggleMinimise()
    {
        SetMinimised(!_isMinimised);
    }


    //reload scene (resets everything)
    private void OnRestartClicked()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    //for minimizing and maximizing DDAPanel
    private void SetMinimised(bool minimise)
    {
        _isMinimised = minimise;

        if (contentArea != null)
        {
            contentArea.SetActive(!minimise);
        }
        if (toggleButtonLabel != null)
        {
            if (minimise)
            {
                toggleButtonLabel.text = "<";
            }
            else
            {
                toggleButtonLabel.text = ">";
            }
        }
        if (ddaPanel != null)
        {
            Vector2 sz = ddaPanel.sizeDelta;
            if (minimise)
            {
                sz.x = 40f;
            }
            else
            {
                sz.x = 1920f;
            }
            ddaPanel.sizeDelta = sz;
        }
    }


    private void BuildSliderDescriptors()
    {
        if (ddaController == null)
        {
            return;
        }

        _descriptors.Add(new SliderDesc
        {
            label = "Update Interval (s)",
            min = 1f,
            max = 30f,
            getter = () => ddaController.updateInterval,
            setter = v => ddaController.updateInterval = v
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Alpha (Sg smooth)",
            min = 0f,
            max = 1f,
            getter = () => ddaController.alpha,
            setter = v => ddaController.alpha = v
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Beta (Sa smooth)",
            min = 0f,
            max = 1f,
            getter = () => ddaController.beta,
            setter = v => ddaController.beta = v
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Session Max (s)",
            min = 60f,
            max = 3600f,
            getter = () => ddaController.sessionMax,
            setter = v => ddaController.sessionMax = v
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Max Speed",
            min = 5f,
            max = 50f,
            getter = () => ddaController.maxSpeed,
            setter = v => ddaController.maxSpeed = v
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Max Obstacle Chance",
            min = 0f,
            max = 1f,
            getter = () => ddaController.maxSpawnChance,
            setter = v => ddaController.maxSpawnChance = v
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Max Small Slide Wt",
            min = 0f,
            max = 100f,
            getter = () => ddaController.smallSlideWeightRange.y,
            setter = v => { var r = ddaController.smallSlideWeightRange; r.y = v; ddaController.smallSlideWeightRange = r; }
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Max Medium Slide Wt",
            min = 0f,
            max = 100f,
            getter = () => ddaController.medSlideWeightRange.y,
            setter = v => { var r = ddaController.medSlideWeightRange; r.y = v; ddaController.medSlideWeightRange = r; }
        });

        _descriptors.Add(new SliderDesc
        {
            label = "Max Big Slide Wt",
            min = 0f,
            max = 100f,
            getter = () => ddaController.bigSlideWeightRange.y,
            setter = v => { var r = ddaController.bigSlideWeightRange; r.y = v; ddaController.bigSlideWeightRange = r; }
        });
    }
    private void BuildSliderUI()
    {
        if (sliderContent == null)
        {
            Debug.LogWarning("sliderContent not assigned. Sliders are disabled.");
            return;
        }

        //add VerticalLayoutGroup so rows stack automatically
        var vlg = sliderContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.spacing = 8f;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandWidth = false;

        //add ContentSizeFitter to auto resize the content height so the ScrollRect can scroll
        var csf = sliderContent.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;


        foreach (var sliderDesc in _descriptors)
        {
            GameObject row = BuildDefaultRow(sliderContent);

            TMP_Text nameLabel = FindInChildren<TMP_Text>(row, "NameLabel");
            Slider slider = row.GetComponentInChildren<Slider>();
            TMP_Text valueLabel = FindInChildren<TMP_Text>(row, "ValueLabel");

            if (nameLabel != null)
            {
                nameLabel.text = sliderDesc.label;
            }

            if (slider != null)
            {
                slider.minValue = sliderDesc.min;
                slider.maxValue = sliderDesc.max;
                slider.value = sliderDesc.getter.Invoke();

                SliderDesc currentSlider = sliderDesc; //ensure the current slider controls the current variable (or else will all control last variable)
                slider.onValueChanged.AddListener(v => currentSlider.setter.Invoke(v)); //whenever slider moves, call setter
            }

            _sliders.Add(slider);
            _valueLabels.Add(valueLabel);
        }
    }


    private GameObject BuildDefaultRow(RectTransform parent)
    {
        const float fontSize = 24f;
        const float spacing = 12f;

        //use standardized widths to ensure multiple rows have the same alignment
        const float headWidth = 200f;
        const float tailWidth = 100f;

        var sliderRow = new GameObject("SliderRow", typeof(RectTransform)); //container for the labels and slider
        sliderRow.transform.SetParent(parent, false); //set sliderContent as parent

        //use HorizontalLayoutGroup to automatically organize position
        var hlg = sliderRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.padding = new RectOffset(5, 5, 0, 0);
        hlg.childControlHeight = true;
        hlg.childControlWidth = true;      //allows LayoutElements to check and modify width
        hlg.childForceExpandWidth = false; //prevents forcing equal sizes
        hlg.childAlignment = TextAnchor.MiddleLeft;

        //ensure the row takes up vertical space
        sliderRow.AddComponent<LayoutElement>().preferredHeight = fontSize * 1.8f;

        //name label
        var nameLabel = new GameObject("NameLabel", typeof(RectTransform));
        nameLabel.transform.SetParent(sliderRow.transform, false);
        var nameText = nameLabel.AddComponent<TextMeshProUGUI>();
        nameText.fontSize = fontSize;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Ellipsis; //add ... at the end if text too long

        var nameLE = nameLabel.AddComponent<LayoutElement>();
        nameLE.preferredWidth = headWidth; //use the same fixed width to ensure all sliders align
        nameLE.flexibleWidth = 0;          //the head will not become bigger (if text has not enough space to show)

        //slider
        var sliderGO = new GameObject("Slider", typeof(RectTransform));
        sliderGO.transform.SetParent(sliderRow.transform, false);
        CreateMinimalSlider(sliderGO);

        var sliderLE = sliderGO.AddComponent<LayoutElement>();
        sliderLE.minWidth = 50f;     //the minimum width for slider
        sliderLE.flexibleWidth = 1;  //if there are more spaces, slider takes up all the remaining space

        //value label
        var valueLabel = new GameObject("ValueLabel", typeof(RectTransform));
        valueLabel.transform.SetParent(sliderRow.transform, false);
        var valueText = valueLabel.AddComponent<TextMeshProUGUI>();
        valueText.fontSize = fontSize;
        valueText.color = new Color(0.9f, 0.85f, 0.5f);
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        valueText.textWrappingMode = TextWrappingModes.NoWrap;

        var valLE = valueLabel.AddComponent<LayoutElement>();
        valLE.preferredWidth = tailWidth; //use same width as set as above
        valLE.flexibleWidth = 0;          //will not become bigger, same as head

        return sliderRow;
    }

    private static Slider CreateMinimalSlider(GameObject parent)
    {
        //background of the slider
        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent.transform, false);
        bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f);
        var bgRectTransform = bg.GetComponent<RectTransform>();
        bgRectTransform.anchorMin = Vector2.zero;
        bgRectTransform.anchorMax = Vector2.one;
        bgRectTransform.sizeDelta = Vector2.zero;

        //container for the fill area of the slider 
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(parent.transform, false);
        var fillAreaRectTransform = fillArea.GetComponent<RectTransform>();
        fillAreaRectTransform.anchorMin = new Vector2(0f, 0.25f);
        fillAreaRectTransform.anchorMax = new Vector2(1f, 0.75f);
        fillAreaRectTransform.sizeDelta = new Vector2(-10f, 0f);
        fillAreaRectTransform.anchoredPosition = new Vector2(5f, 0f);

        //fill area of the slider (progress bar)
        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        fill.GetComponent<Image>().color = new Color(0.35f, 0.65f, 0.9f);
        var fillRectTransform = fill.GetComponent<RectTransform>();
        fillRectTransform.anchorMin = Vector2.zero;
        fillRectTransform.anchorMax = Vector2.one;
        fillRectTransform.sizeDelta = Vector2.zero;

        //the path for the handle of the slider (so it even when dragged over the slider, it will not go over it)
        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(parent.transform, false);
        var handleAreaRectTranform = handleArea.GetComponent<RectTransform>();
        handleAreaRectTranform.anchorMin = Vector2.zero;
        handleAreaRectTranform.anchorMax = Vector2.one;
        handleAreaRectTranform.sizeDelta = new Vector2(-10f, 0f);
        handleAreaRectTranform.anchoredPosition = new Vector2(5f, 0f);

        //the handle of the slider
        var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        handle.GetComponent<Image>().color = Color.white;
        handle.GetComponent<RectTransform>().sizeDelta = new Vector2(10f, 0f);

        //connect the components above into a slider
        parent.AddComponent<Image>().color = Color.clear;
        var slider = parent.AddComponent<Slider>();
        slider.fillRect = fill.GetComponent<RectTransform>();
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        return slider;
    }

    private void SyncSliderValueLabels()
    {
        for (int i = 0; i < _descriptors.Count && i < _valueLabels.Count; i++)
        {
            var label = _valueLabels[i];
            var slider = _sliders[i];
            if (label == null)
            {
                continue;
            }

            float liveData;
            if (_descriptors[i].getter != null && ddaController != null)
            {
                liveData = _descriptors[i].getter();
            }
            else
            {

                liveData = slider.value;
            }


            label.text = liveData.ToString("F3");

            //sync slider if the value changed externally
            if (slider != null)
            {
                slider.SetValueWithoutNotify(Mathf.Clamp(liveData, slider.minValue, slider.maxValue));
            }
        }
    }


    private static void SetLabel(TMP_Text label, string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    private static void TintLabel(TMP_Text label, Color color)
    {
        if (label != null)
        {
            label.color = color;
        }
    }

    //for fuzzy firing
    private static readonly string[] FuzzyLabelNames = { "DL", "DS", "NC", "IS", "IL" };
    private static string FuzzyLabel(float delta, float[] crispValues)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        //check which crisp value is closest to delta (player performance) 
        for (int i = 0; i < crispValues.Length; i++)
        {
            float d = Mathf.Abs(delta - crispValues[i]);
            if (d < bestDist)
            {
                bestDist = d; 
                best = i;
            }
        }
        // Show the raw centroid alongside the snapped label so small IS/DS outputs near NC=0 are never silently hidden
        string sign;
        if (delta >= 0f)
        {
            sign = "+"; 
        }
        else
        {
            sign = "-"; 
        }

        return $"{FuzzyLabelNames[best]}({sign}{delta:F3})";
    }

    //used to find a specific game object (child) from parent
    private static T FindInChildren<T>(GameObject root, string childName) where T : Component
    {
        Transform t = root.transform.Find(childName);
        if (t != null)
        {
            var c = t.GetComponent<T>(); //return the component from the child
            if (c != null)
            {
                return c;
            }
        }
        return root.GetComponentInChildren<T>();
    }
}