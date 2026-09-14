using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.UI;

public class GraphRenderer : MonoBehaviour
{
    [Header("Render On Target")]
    [SerializeField] private RawImage rawImage;

    [Header("Graph Dimensions")]
    [Tooltip("Width in pixels of the graph texture")]
    [SerializeField] private int textureWidth = 512;
    [Tooltip("Height in pixels of the graph texture")]
    [SerializeField] private int textureHeight = 200;

    [Header("Y-Axis Range")]
    [Tooltip("When true the Y axis automatically expands to fit incoming samples.")]
    [SerializeField] private bool autoScale = false;
    [SerializeField] private float yMin = 0f;
    [SerializeField] private float yMax = 1f;

    [Header("Appearance")]
    [SerializeField] private Color backgroundColor = new Color(0.08f, 0.08f, 0.12f, 0.95f);
    [SerializeField] private Color gridLineColor = new Color(1f, 1f, 1f, 0.08f);
    [Tooltip("Number of horizontal grid lines drawn across the graph")]
    [SerializeField] private int gridRows = 4;

    //colours
    private Color[] seriesColors = new Color[]
    {
        new Color(0, 0, 0),  
        new Color(0, 0, 0),   
        new Color(0, 0, 0),  
        new Color(0, 0, 0),  
    };

    private const int MAX_SERIES = 4;
    private const int LINE_THICKNESS = 4;  //pixel radius of each plotted line

    private Texture2D _texture; //graph is drawn here
    private Color[] _clearBuffer; //clean graph, no lines or points, jz background and grid lines
    private float[][] _samples; //[value of sample][index]
    private bool[][] _hasSample; //check whether a column has been written
    private int _writeHead = 0; //current column being written
    private bool _dirty = true; //check if needs redraw


    private void Awake()
    {
        //allocate texture
        _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        _texture.filterMode = FilterMode.Point; //make pixels look crisp
        _texture.wrapMode = TextureWrapMode.Clamp; //prevents texture from going beyond above and appear from bottom

        if (rawImage == null)
        {
            rawImage = GetComponent<RawImage>();
        }

        if (rawImage != null)
        {
            rawImage.texture = _texture;
        }
        else
        {
            Debug.LogWarning("No RawImage found");
        }

        //create containers for the data points
        _samples = new float[MAX_SERIES][];
        _hasSample = new bool[MAX_SERIES][];
        for (int s = 0; s < MAX_SERIES; s++)
        {
            //store with an array exactly as the width size so that each pixel can be drawn
            _samples[s] = new float[textureWidth]; 
            _hasSample[s] = new bool[textureWidth];
        }

        BuildClearBuffer();
        RedrawTexture();
    }

    private void OnDestroy()
    {
        if (_texture != null)
        {
            Destroy(_texture);
        }
    }

    //push a new data point for the given series
    public void AddSample(int seriesIndex, float value)
    {
        if (seriesIndex < 0 || seriesIndex >= MAX_SERIES)
        {
            return;
        }

        //when value is above the graph limit
        if (autoScale)
        {
            //redraw a bigger one
            if (value < yMin)
            {
                yMin = value; 
                BuildClearBuffer();
            }
            if (value > yMax)
            {
                yMax = value; 
                BuildClearBuffer();
            }
        }

        _samples[seriesIndex][_writeHead] = value;
        _hasSample[seriesIndex][_writeHead] = true;

        _dirty = true;
    }

    //shifts the graph left by one pixel column.
    public void AdvanceColumn()
    {
        _writeHead = (_writeHead + 1) % textureWidth;

        //clear the new column so old data wont appear
        for (int s = 0; s < MAX_SERIES; s++)
        {
            _hasSample[s][_writeHead] = false;
        }

        _dirty = true;
    }

    public void SetSeriesColor(int index, Color color)
    {
        if (index >= 0 && index < seriesColors.Length)
        {
            seriesColors[index] = color;
        }
    }


    public void Flush()
    {
        if (_dirty)
        {
            RedrawTexture();
        }
    }


    private void BuildClearBuffer()
    {
        _clearBuffer = new Color[textureWidth * textureHeight];

        //background fill
        for (int i = 0; i < _clearBuffer.Length; i++)
        {
            _clearBuffer[i] = backgroundColor;
        }

        //horizontal grid lines
        for (int g = 0; g <= gridRows; g++)
        {
            int pixelYCoords = Mathf.RoundToInt((float)g / gridRows * (textureHeight - 1));
            for (int x = 0; x < textureWidth; x++)
            {
                _clearBuffer[pixelYCoords * textureWidth + x] = gridLineColor;
            }
        }
    }

    private void RedrawTexture()
    {
        //clone clean buffer
        Color[] pixels = (Color[])_clearBuffer.Clone();

        //draw each series as a connected polyline
        for (int s = 0; s < MAX_SERIES; s++)
        {
            Color lineColor;
            if (s < seriesColors.Length)
            {
                //use custom colour
                lineColor = seriesColors[s];
            }
            else
            {
                lineColor = Color.white;
            }

            int prevColXCoords = -1;
            int prevPixelYCoords = -1;

            for (int colXCoords = 0; colXCoords < textureWidth; colXCoords++)
            {
                int dataCol = (colXCoords + _writeHead + 1) % textureWidth; //realign, ensure the newest data is on right

                if (!_hasSample[s][dataCol])
                {
                    //if data suddenly stopped, skip it
                    prevColXCoords = -1;
                    prevPixelYCoords = -1;
                    continue;
                }

                float value = _samples[s][dataCol];
                float valueIn0To1 = Mathf.InverseLerp(yMin, yMax, value);
                int pixelYCoords = Mathf.RoundToInt(valueIn0To1 * (textureHeight - 1));
                if (prevColXCoords >= 0)
                {
                    //draw a line for connected point
                    DrawThickSegment(pixels, prevColXCoords, prevPixelYCoords, colXCoords, pixelYCoords, lineColor);
                }
                else
                {
                    //if no previous point
                    PaintThickPixel(pixels, colXCoords, pixelYCoords, lineColor);
                }

                prevColXCoords = colXCoords;
                prevPixelYCoords = pixelYCoords;
            }
        }

        //apply changes
        _texture.SetPixels(pixels);
        _texture.Apply();
        _dirty = false;
    }


    //draws a line from (x0,y0) to (x1,y1) using Bresenham's algorithm
    private void DrawThickSegment(Color[] pixels, int x0, int y0, int x1, int y1, Color col)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        int x = x0, y = y0;
        while (true)
        {
            PaintThickPixel(pixels, x, y, col);

            if (x == x1 && y == y1)
            {
                break;
            }

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y += sy;
            }
        }
    }


    //paints a single pixel at (px, py) 
    private void PaintThickPixel(Color[] pixels, int px, int py, Color col)
    {
        if (px < 0 || px >= textureWidth)
        {
            return;
        }
        for (int dy = -LINE_THICKNESS; dy <= LINE_THICKNESS; dy++)
        {
            int ry = py + dy;
            if (ry >= 0 && ry < textureHeight)
            {
                pixels[ry * textureWidth + px] = col;
            }
        }
    }
}