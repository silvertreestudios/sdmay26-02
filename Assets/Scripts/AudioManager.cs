using UnityEngine;

public class AudioManager : SingletonMonoBehaviour<AudioManager>
{
    [Header("Audio Sources")]
    [SerializeField]
    private AudioSource musicSource;

    [SerializeField]
    private AudioSource sfxSource;

    [Header("Audio Clips")]
    public AudioClip backgroundMusic;
    public AudioClip onHitSFX;
    public AudioClip onMissSFX;
    public AudioClip ZombieAttackSFX;
    public AudioClip onDeathZombieSFX;
    public AudioClip onDeathPlayerSFX;
    public AudioClip onMoveSFX;
    public AudioClip onWinSFX;
    public AudioClip onLoseSFX;

    void OnEnable()
    {
        //put listeners for events here
        OnStepEnd.AddListener(PlayStepSFX);
        OnDamageDealt.AddListener(PlayDamageSFX);
        OnDeath.AddListener(PlayDeathSFX);
        OnAttackMiss.AddListener(PlayMissSFX);
        OnCombatOutcome.AddListener(PlayWinLoseSFX);
    }

    void OnDisable()
    {
        //remove listeners for events here
        OnStepEnd.RemoveListener(PlayStepSFX);
        OnDamageDealt.RemoveListener(PlayDamageSFX);
        OnDeath.RemoveListener(PlayDeathSFX);
        OnAttackMiss.RemoveListener(PlayMissSFX);
        OnCombatOutcome.RemoveListener(PlayWinLoseSFX);
    }

    void Start()
    {
        musicSource.clip = backgroundMusic;
        // Set the music to loop
        musicSource.loop = true;
        musicSource.Play();
    }

    //play step sfx
    void PlayStepSFX(Vector3 position)
    {
        sfxSource.pitch = Random.Range(0.9f, 1.1f);
        sfxSource.PlayOneShot(onMoveSFX);
    }

    //play damage sfx
    void PlayDamageSFX(string type)
    {
        switch (type.ToLower())
        {
            case "bludgeoning":
                sfxSource.PlayOneShot(ZombieAttackSFX);
                break;
            case "slashing":
                sfxSource.PlayOneShot(onHitSFX);
                break;
            case "piercing":
                sfxSource.PlayOneShot(onHitSFX);
                break;
            default:
                sfxSource.PlayOneShot(onHitSFX);
                break;
        }
    }

    //play miss sfx
    public void PlayMissSFX(GameObject attacker)
    {
        sfxSource.PlayOneShot(onMissSFX);
    }

    //play death
    void PlayDeathSFX(GameObject deceased)
    {
        string team = deceased.GetComponent<Team>()?.Name;
        switch (team?.ToLower())
        {
            case "players":
                sfxSource.PlayOneShot(onDeathPlayerSFX);
                break;
            case "zombies":
                sfxSource.PlayOneShot(onDeathZombieSFX);
                break;
            default:
                Debug.LogWarning("[AudioManager] Unknown team: " + team);
                sfxSource.PlayOneShot(onDeathPlayerSFX);
                break;
        }
    }

    void PlayWinLoseSFX(bool playerWon)
    {
        if (playerWon)
        {
            sfxSource.PlayOneShot(onWinSFX);
        }
        else
        {
            sfxSource.PlayOneShot(onLoseSFX);
        }
    }
}
