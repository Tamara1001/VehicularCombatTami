using UnityEngine;
using System.Collections;

/// <summary>
/// Singleton simple para manejar un panel negro de transición.
/// </summary>
public class UI_ScreenFader : MonoBehaviour
{
    public static UI_ScreenFader Instance { get; private set; }

    [Tooltip("El CanvasGroup asociado a la imagen negra que tapa la pantalla")]
    public CanvasGroup fadeGroup;

    private void Awake()
    {
        Instance = this;
        // Nos aseguramos de que arranque transparente y sin bloquear clics
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 0f;
            fadeGroup.blocksRaycasts = false;
        }
    }

    public void FadeTo(float targetAlpha, float duration)
    {
        if (fadeGroup == null) return;

        // Bloqueamos clics si estamos oscureciendo la pantalla
        fadeGroup.blocksRaycasts = targetAlpha > 0f;

        StopAllCoroutines();
        StartCoroutine(FadeRoutine(targetAlpha, duration));
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        float startAlpha = fadeGroup.alpha;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            fadeGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, time / duration);
            yield return null;
        }

        fadeGroup.alpha = targetAlpha;
    }
}