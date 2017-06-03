using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class SceneSwitcher : MonoBehaviour
{

    public void SwitchScenes()
    {
        int nextLevel = (SceneManager.GetActiveScene().buildIndex + 1) % SceneManager.sceneCountInBuildSettings;
        SceneManager.LoadScene(nextLevel);
    }
}
