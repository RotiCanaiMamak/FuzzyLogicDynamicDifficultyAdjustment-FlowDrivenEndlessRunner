using UnityEngine;
using UnityEngine.U2D;

[RequireComponent(typeof(SpriteShapeController))]
public class TerrainChunk : MonoBehaviour
{
    [Header("Shape")]
    public float depth = 15f;

    [HideInInspector] public float chunkWidth;
    [HideInInspector] public float startY;
    [HideInInspector] public float endY;
    [HideInInspector] public float cliffHeight;

    private SpriteShapeController ssc;

    public float RightEdgeY
    {
        get
        {
            if (cliffHeight > 0f)
            {
                return startY - cliffHeight;
            }
            else
            {
                return endY;
            }
        }
    }

    private Vector3 _b0; //surface entry (top-left,  local)
    private Vector3 _b1; //right tangent of entry point  (control point)
    private Vector3 _b2; //left  tangent of exit  point  (control point)
    private Vector3 _b3; //surface exit (top-right, local)
    private bool _bezierReady = false;

    void Awake()
    {
        ssc = GetComponent<SpriteShapeController>();
    }

    public void Build(Vector3 entryTangent, Vector3 exitTangent)
    {
        ssc.splineDetail = 64; //ensure that the physics collider is smooth

        Spline spline = ssc.spline;
        spline.Clear(); //wipe old data before building new

        float x0 = 0f; //bottom left edge
        float x3 = chunkWidth; //bottom right edge

        float localTopLeft = 0f;
        float localTopRight = RightEdgeY - startY;

        //point 0 (bottom right)
        spline.InsertPointAt(0, new Vector3(x0, localTopLeft - depth, 0f));
        spline.SetTangentMode(0, ShapeTangentMode.Linear); //tells unity that the lines connected to this point is straight
                                                           // bottom left and bottom right connects to a straight line

        //use c1 continuity
        float tangentLength = chunkWidth * 0.33f;

        // point 1 (top left)
        //entryTangent = previous chunk's exitTangent, enforcing C1 at the seam for seamless continuity
        spline.InsertPointAt(1, new Vector3(x0, localTopLeft, 0f));
        spline.SetTangentMode(1, ShapeTangentMode.Continuous); //allow lines to curve through the points
        spline.SetLeftTangent(1, -entryTangent * tangentLength);
        spline.SetRightTangent(1, entryTangent * tangentLength);

        // point 2 (top right)
        spline.InsertPointAt(2, new Vector3(x3, localTopRight, 0f));
        spline.SetTangentMode(2, ShapeTangentMode.Continuous); //same continous as above (so no sharp angle)
        spline.SetLeftTangent(2, -exitTangent * tangentLength);
        spline.SetRightTangent(2, exitTangent * tangentLength);

        // point 3 (bottom right)
        spline.InsertPointAt(3, new Vector3(x3, localTopRight - depth, 0f));
        spline.SetTangentMode(3, ShapeTangentMode.Linear);

        ssc.RefreshSpriteShape(); //bake into visible terraini

        _b0 = new Vector3(x0, localTopLeft, 0f); //top left
        _b1 = _b0 + entryTangent * tangentLength; //the pulling force that starts the curve at the beginning of the chunk
        _b2 = new Vector3(x3, localTopRight, 0f) - exitTangent * tangentLength; //the pulling force that guides the curve into the finish line
        _b3 = new Vector3(x3, localTopRight, 0f); //top right
        _bezierReady = true;
    }


    public void RefreshAfterShift()
    {
        if (ssc != null)
        {
            ssc.RefreshSpriteShape();
            ssc.UpdateSpriteShapeParameters();
        }
    }


    public bool EvaluateSurface(float progressOfTerrainChunk, out Vector3 worldPos, out Vector3 worldNormal)
    {
        if (!_bezierReady)
        {
            worldPos = transform.position;
            worldNormal = Vector3.up;
            return false;
        }

        progressOfTerrainChunk = Mathf.Clamp01(progressOfTerrainChunk);

        float howFarFromTheEnd = 1f - progressOfTerrainChunk;

        //cubic bezier point (cubic bezier equation)
        Vector3 localPos = howFarFromTheEnd * howFarFromTheEnd * howFarFromTheEnd * _b0 
                         + 3f * howFarFromTheEnd * howFarFromTheEnd * progressOfTerrainChunk * _b1 // helps to steer away from the start
                         + 3f * howFarFromTheEnd * progressOfTerrainChunk * progressOfTerrainChunk * _b2 //helps to steer the curve to the end
                         + progressOfTerrainChunk * progressOfTerrainChunk * progressOfTerrainChunk * _b3;

        //derivative
        //formula: B'(t) = 3 [ u^2*(B1-B0) + 2ut*(B2-B1) + t^2*(B3-B2) ]
        Vector3 deriv = 3f * (howFarFromTheEnd * howFarFromTheEnd * (_b1 - _b0)
                            + 2f * howFarFromTheEnd * progressOfTerrainChunk * (_b2 - _b1)
                            + progressOfTerrainChunk * progressOfTerrainChunk * (_b3 - _b2));

        //upward normal: rotate tangent 90° counter clockwise in the XY plane
        Vector3 localNormal = new Vector3(-deriv.y, deriv.x, 0f).normalized;

        //a fail safe when the forward direction (0,0,0)
        if (localNormal.sqrMagnitude < 0.01f)
        {
            localNormal = Vector3.up;
        }

        worldPos = transform.TransformPoint(localPos);
        worldNormal = transform.TransformDirection(localNormal);
        return true;
    }
}