using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Clock : MonoBehaviour
{
	public Transform hoursTransform;
	public Transform minutesTransform;
	public Transform secondsTransform;
	public bool continuous;

	const float degreesPerHour = 30.0f;
	const float degreesPerMinute = 6.0f;
	const float degreesPerSecond = 6.0f;

	void Update()
	{
		if (continuous)
		{
			UpdateContinuous();
		}
		else
		{
			UpdateDiscrete();
		}
	}

	void UpdateContinuous()
	{
		System.TimeSpan time = System.DateTime.Now.TimeOfDay;
		hoursTransform.localRotation = Quaternion.Euler(0.0f, (float)time.TotalHours * degreesPerHour, 0.0f);
		minutesTransform.localRotation = Quaternion.Euler(0.0f, (float)time.TotalMinutes * degreesPerMinute, 0.0f);
		secondsTransform.localRotation = Quaternion.Euler(0.0f, (float)time.TotalSeconds * degreesPerSecond, 0.0f);
	}

	void UpdateDiscrete()
	{
		System.DateTime time = System.DateTime.Now;
		hoursTransform.localRotation = Quaternion.Euler(0.0f, time.Hour * degreesPerHour, 0.0f);
		minutesTransform.localRotation = Quaternion.Euler(0.0f, time.Minute * degreesPerMinute, 0.0f);
		secondsTransform.localRotation = Quaternion.Euler(0.0f, time.Second * degreesPerSecond, 0.0f);
	}
}
