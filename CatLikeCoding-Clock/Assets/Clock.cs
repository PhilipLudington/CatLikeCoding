using UnityEngine;
using System.Collections;
using System;

public class Clock : MonoBehaviour
{
    public Transform Hours;
    public Transform Minutes;
    public Transform Seconds;
    public bool IsAnalogy;

    private const float hoursToDegrees = 360 / 12.0f;
    private const float minutesToDegrees = 360 / 60.0f;
    private const float secondsToDegrees = 360 / 60.0f;

    // Use this for initialization
    private void Start()
    {

    }

    // Update is called once per frame
    private void Update()
    {
        if (IsAnalogy)
        {
            TimeSpan timeSpan = DateTime.Now.TimeOfDay;

            Hours.transform.localRotation = Quaternion.Euler(0, 0, (float)timeSpan.TotalHours * -hoursToDegrees);
            Minutes.transform.localRotation = Quaternion.Euler(0, 0, (float)timeSpan.TotalMinutes * -minutesToDegrees);
            Seconds.transform.localRotation = Quaternion.Euler(0, 0, (float)timeSpan.TotalSeconds * -secondsToDegrees);
        }
        else
        {
            DateTime dateTime = DateTime.Now;

            Hours.transform.localRotation = Quaternion.Euler(0, 0, dateTime.Hour * -hoursToDegrees);
            Minutes.transform.localRotation = Quaternion.Euler(0, 0, dateTime.Minute * -minutesToDegrees);
            Seconds.transform.localRotation = Quaternion.Euler(0, 0, dateTime.Second * -secondsToDegrees);
        }
    }
}
