using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnEffect : MonoBehaviour
{
    [Tooltip("Total duration in seconds of the dissolve animation from solid to fully vanished.")]
    public float spawnEffectTime = 1.6f;

    [Tooltip("Pause delay before looping (not used for one-shot collection dissolves).")]
    public float pause = 1f;

    [Tooltip("Animation curve controlling the dissolve progression (0 = solid at start, 1 = vanished at end).")]
    public AnimationCurve fadeIn = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private float timer = 0f;
    private Renderer _renderer;
    private Material _mat;
    private ParticleSystem ps;
    private bool isDone = false;

    private static readonly int CutoffID = Shader.PropertyToID("_Cutoff");
    private static readonly int CutoffLowerID = Shader.PropertyToID("_cutoff");

    private void Awake()
    {
        InitializeMaterial();
        SetCutoff(0f);
    }

    private void Start()
    {
        timer = 0f;
        isDone = false;

        InitializeMaterial();
        SetCutoff(0f);

        // Configure and start child particle systems
        ps = GetComponentInChildren<ParticleSystem>();
        if (ps != null)
        {
            if (ps.isPlaying)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            var main = ps.main;
            main.duration = spawnEffectTime;

            ps.Play();
        }
    }

    private void Update()
    {
        if (isDone) return;

        timer += Time.deltaTime;
        float progress = Mathf.Clamp01(timer / Mathf.Max(spawnEffectTime, 0.01f));
        float cutoff = fadeIn != null ? Mathf.Clamp01(fadeIn.Evaluate(progress)) : progress;

        SetCutoff(cutoff);

        if (timer >= spawnEffectTime)
        {
            isDone = true;
            SetCutoff(1f);
        }
    }

    private void InitializeMaterial()
    {
        if (_renderer == null)
        {
            _renderer = GetComponent<Renderer>();
        }

        if (_mat == null && _renderer != null)
        {
            _mat = _renderer.material;
        }
    }

    public void SetCutoff(float cutoff)
    {
        if (_mat == null)
        {
            InitializeMaterial();
        }

        if (_mat != null)
        {
            _mat.SetFloat(CutoffID, cutoff);
            _mat.SetFloat(CutoffLowerID, cutoff);
        }
    }

    public void ResetEffect()
    {
        timer = 0f;
        isDone = false;
        SetCutoff(0f);
    }
}

