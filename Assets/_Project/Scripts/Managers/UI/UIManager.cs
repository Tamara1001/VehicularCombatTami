using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Puente visual directo entre la FSM (Máquina de Estados) del GameManager y la capa de la Interfaz de Usuario (UI).
/// Escucha los cambios de estado globales para activar o desactivar los paneles correspondientes.
/// 
/// Reglas de Arquitectura:
/// - No contiene lógica dura de juego ni manipula el tiempo directamente.
/// - Se comunica con el GameManager de forma unidireccional a través de eventos.
/// </summary>
public class UIManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Campos del Inspector — Paneles Base
    // -------------------------------------------------------------------------
    [Header("Paneles Principales de la UI")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject playingHUDPanel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject victoryPanel;

    [Header("Paneles Superpuestos (Overlays)")]
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private GameObject pausePanel;

    [Header("Botones Especiales")]
    [Tooltip("Botón 'Continuar' del Menú Principal. Se desactiva automáticamente " +
             "si no hay una sesión activa (GameManager.HasActiveSession == false).")]
    [SerializeField] private Button continueButton;

    // -------------------------------------------------------------------------
    // Ciclo de Vida de Unity
    // -------------------------------------------------------------------------
    private void OnEnable()
    {
        GameManager.OnStateChanged += HandleStateChanged;
    }

    private void OnDisable()
    {
        GameManager.OnStateChanged -= HandleStateChanged;
    }

    private void Start()
    {
        // ---------------------------------------------------------------
        // Safety check: log any unassigned panel references immediately so
        // missing Inspector wires are caught at startup, not mid-session.
        // ---------------------------------------------------------------
        if (mainMenuPanel   == null) Debug.LogWarning("[UIManager] mainMenuPanel is not assigned in the Inspector!",   this);
        if (playingHUDPanel == null) Debug.LogWarning("[UIManager] playingHUDPanel is not assigned in the Inspector!", this);
        if (pausePanel      == null) Debug.LogWarning("[UIManager] pausePanel is not assigned in the Inspector!",      this);
        if (gameOverPanel   == null) Debug.LogWarning("[UIManager] gameOverPanel is not assigned in the Inspector!",   this);
        if (victoryPanel    == null) Debug.LogWarning("[UIManager] victoryPanel is not assigned in the Inspector!",    this);

        // ---------------------------------------------------------------
        // Bootstrap: read the FSM's current state and apply it immediately.
        //
        // IMPORTANT TIMING NOTE:
        //   On a scene reload via StartNewGame(), this Start() executes
        //   BEFORE GameManager.OnSceneLoaded() fires ChangeState(Playing).
        //   That means at this exact moment CurrentState is still MainMenu,
        //   so the main menu panel will be shown here — and then immediately
        //   replaced when the Playing event arrives via OnStateChanged.
        //   If the Playing event never arrives after this log line, the
        //   problem is upstream in GameManager.OnSceneLoaded / _pendingRestart.
        // ---------------------------------------------------------------
        if (GameManager.Instance != null)
        {
            Debug.Log($"[UIManager] Start() — GameManager found. " +
                      $"Bootstrapping UI to current state: '{GameManager.Instance.CurrentState}'. " +
                      $"(If this is MainMenu right before Playing, that is expected — watch for the next HandleStateChanged log.)", this);
            HandleStateChanged(GameManager.Instance.CurrentState);
        }
        else
        {
            Debug.LogWarning("[UIManager] Start() — GameManager.Instance is null! " +
                             "Defaulting to ShowMainMenu(). Ensure GameManager exists in the scene.", this);
            ShowMainMenu();
        }
    }

    // -------------------------------------------------------------------------
    // Manejador de Eventos de la FSM
    // -------------------------------------------------------------------------
    private void HandleStateChanged(GameManager.GameState newState)
    {
        // ---------------------------------------------------------------
        // DIAGNOSTIC LOG — visible in the Console for every state change.
        // If you never see "→ Playing" here after clicking Play, the event
        // is not reaching UIManager: check GameManager.OnSceneLoaded() and
        // confirm _pendingRestart was set before LoadScene was called.
        // If you see it but the panel is still wrong, a panel reference is
        // null — check the warnings logged by Start() above.
        // ---------------------------------------------------------------
        Debug.Log($"[UIManager] HandleStateChanged → '{newState}'. " +
                  $"Frame: {Time.frameCount}  |  Time: {Time.realtimeSinceStartup:F2}s", this);

        // Close any overlay (e.g. Options) on every state transition.
        CloseOptionsPanel();

        switch (newState)
        {
            case GameManager.GameState.MainMenu:
                ShowMainMenu();
                break;
            case GameManager.GameState.Playing:
                ShowPlayingHUD();
                break;
            case GameManager.GameState.Pause:
                ShowPause();
                break;
            case GameManager.GameState.GameOver:
                ShowGameOver();
                break;
            case GameManager.GameState.Victory:
                ShowVictory();
                break;
            default:
                Debug.LogWarning($"[UIManager] Estado no contemplado recibido: '{newState}'. " +
                                 "Agrega un case para este estado en HandleStateChanged.", this);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Métodos de Control de Paneles (Privados)
    // -------------------------------------------------------------------------
    private void ShowMainMenu()
    {
        mainMenuPanel?.SetActive(true);
        playingHUDPanel?.SetActive(false);
        pausePanel?.SetActive(false);
        gameOverPanel?.SetActive(false);
        victoryPanel?.SetActive(false);

        // Enable the Continue button only when there is an ongoing session to return to.
        if (continueButton != null)
            continueButton.interactable = GameManager.Instance != null &&
                                          GameManager.Instance.HasActiveSession;
    }

    private void ShowPlayingHUD()
    {
        mainMenuPanel?.SetActive(false);
        playingHUDPanel?.SetActive(true);
        pausePanel?.SetActive(false);
        gameOverPanel?.SetActive(false);
        victoryPanel?.SetActive(false);
    }

    private void ShowPause()
    {
        mainMenuPanel?.SetActive(false);
        playingHUDPanel?.SetActive(false);
        pausePanel?.SetActive(true);
        gameOverPanel?.SetActive(false);
        victoryPanel?.SetActive(false);
    }

    private void ShowGameOver()
    {
        mainMenuPanel?.SetActive(false);
        playingHUDPanel?.SetActive(false);
        pausePanel?.SetActive(false);
        gameOverPanel?.SetActive(true);
        victoryPanel?.SetActive(false);
    }

    private void ShowVictory()
    {
        mainMenuPanel?.SetActive(false);
        playingHUDPanel?.SetActive(false);
        pausePanel?.SetActive(false);
        gameOverPanel?.SetActive(false);
        victoryPanel?.SetActive(true);
    }

    private void CloseOptionsPanel()
    {
        optionsPanel?.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Callbacks Públicos para Botones (UI Event Triggers)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Vinculado al botón "Jugar" o "Nueva Partida" del Menú Principal.
    /// </summary>
    /// <summary>
    /// Vinculado al botón "Continuar" del Menú Principal.
    /// Solo es interactuable cuando <see cref="GameManager.HasActiveSession"/> es true.
    /// </summary>
    public void OnContinueClicked()
    {
        GameManager.Instance.ContinueGame();
    }

    public void OnPlayClicked()
    {
        GameManager.Instance.StartNewGame();
    }

    /// <summary>
    /// Vinculado al botón "Reanudar" dentro del menú de Pausa.
    /// </summary>
    public void OnResumeButtonClicked()
    {
        GameManager.Instance.ResumeFromPause();
    }

    /// <summary>
    /// Vinculado al botón "Reintentar" de la pantalla de Game Over.
    /// </summary>
    public void OnRestartButtonClicked()
    {
        GameManager.Instance.StartNewGame();
    }

    /// <summary>
    /// Vinculado al botón "Volver al Menú" desde Pausa o Game Over.
    /// </summary>
    public void OnReturnToMenuClicked()
    {
        GameManager.Instance.ReturnToMainMenu();
    }

    /// <summary>
    /// Vinculado opcionalmente a un botón de pausa en pantalla dentro del HUD.
    /// </summary>
    public void OnPauseButtonClicked()
    {
        GameManager.Instance.ChangeState(GameManager.GameState.Pause);
    }

    public void OnOptionsClicked()
    {
        optionsPanel?.SetActive(true);
    }

    public void OnCloseOptionsClicked()
    {
        CloseOptionsPanel();
    }

    /// <summary>
    /// Vinculado al botón "Salir" en el Menú Principal.
    /// </summary>
    public void OnQuitClicked()
    {
#if UNITY_EDITOR
        Debug.Log("[UIManager] OnQuitClicked — Application.Quit() suprimido en el Editor.");
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}