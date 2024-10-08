﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using iXRLib;
using UnityEngine;
using XRDM.SDK.External.Unity;

[DefaultExecutionOrder(1)]
public class Authentication : SdkBehaviour
{
    private static Authentication _instance;
    private static string _orgId;
    private static string _deviceId;
    private static string _authSecret;
    private static string _userId;
    private static string _appId;
    private static Partner _partner = Partner.eNone;
    
    public static void Initialize()
    {
        if (_instance != null) return;
        
        var singletonObject = new GameObject("Authentication");
        _instance = singletonObject.AddComponent<Authentication>();
        DontDestroyOnLoad(singletonObject);
    }
    
    protected override void OnEnable()
    {
#if UNITY_ANDROID
        base.OnEnable();
        var callBack = new Callback();
        Connect(callBack);
#endif
    }

    private static void CheckArborInfo()
    {
        string orgId = Callback.Service.GetOrgId();
        if (string.IsNullOrEmpty(orgId)) return;

        _partner = Partner.eArborXR;
        _orgId = orgId;
        _deviceId = Callback.Service.GetDeviceId();
        _authSecret = Callback.Service.GetFingerprint();
        _userId = Callback.Service.GetAccessToken();
    }
    
    private sealed class Callback : IConnectionCallback
    {
        public static ISdkService Service;
        
        public void OnConnected(ISdkService service) => Service = service;

        public void OnDisconnected(bool isRetrying) => Service = null;
    }
    
    private void Start()
    {
#if UNITY_ANDROID
        CheckArborInfo();
#endif
        if (GetDataFromConfig())
        {
            SetSessionData();
            Authenticate();
        }
    }
    
    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
			if (iXRAuthentication.TokenExpirationImminent())
            {
                Authenticate();
            }
        }
        else
        {
            iXRInit.ForceSendUnsentSynchronous();
        }
    }

    private static bool GetDataFromConfig()
    {
        const string appIdPattern = "^[A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12}$";
        if (!Regex.IsMatch(Configuration.instance.appID, appIdPattern))
        {
            Debug.LogError("iXRLib - Invalid Application ID. Cannot authenticate.");
            return false;
        }

        _appId = Configuration.instance.appID;

        if (_partner == Partner.eArborXR) return true; // the rest of the values are set by Arbor
        
        _orgId = Configuration.instance.orgID;
        if (string.IsNullOrEmpty(_orgId))
        {
            Debug.LogError("iXRLib - Missing Organization ID. Cannot authenticate.");
            return false;
        }
        
        const string orgIdPattern = "^[A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12}$";
        if (!Regex.IsMatch(_orgId, orgIdPattern))
        {
            Debug.LogError("iXRLib - Invalid Organization ID. Cannot authenticate.");
            return false;
        }

        _authSecret = Configuration.instance.authSecret;
        if (string.IsNullOrEmpty(_authSecret))
        {
            Debug.LogError("iXRLib - Missing Auth Secret. Cannot authenticate.");
            return false;
        }
        
        _deviceId = SystemInfo.deviceUniqueIdentifier;

        return true;
    }

    public static void Authenticate()
    {
        var result = iXRInit.Authenticate(_appId, _orgId, _deviceId, _authSecret, _partner);
        if (result == iXRResult.Ok)
        {
            Debug.Log("iXRLib - Authenticated successfully");
        }
        else
        {
            Debug.LogError($"iXRLib - Authentication failed : {result}");
        }
    }

    private static void SetSessionData()
    {
        //TODO Device Type
        
        iXRAuthentication.Partner = _partner;
        if (!string.IsNullOrEmpty(_userId)) iXRAuthentication.UserId = _userId;
        
        iXR.TelemetryEntry("OS Version", $"Version={SystemInfo.operatingSystem}");
        iXRAuthentication.OsVersion = SystemInfo.operatingSystem;
        
        var currentAssembly = Assembly.GetExecutingAssembly();
        AssemblyName[] referencedAssemblies = currentAssembly.GetReferencedAssemblies();
        foreach (AssemblyName assemblyName in referencedAssemblies)
        {
            if (assemblyName.Name == "XRDM.SDK.External.Unity")
            {
                iXR.TelemetryEntry("XRDM Version", $"Version={assemblyName.Version}");
                iXRAuthentication.XrdmVersion = assemblyName.Version.ToString();
                break;
            }
        }
        
        //TODO Geolocation

        iXR.TelemetryEntry("Application Version", $"Version={Application.version}");
        iXRAuthentication.AppVersion = Application.version;
        
        iXR.TelemetryEntry("Unity Version", $"Version={Application.unityVersion}");
        iXRAuthentication.UnityVersion = Application.unityVersion;

        SetIPAddress();
    }

    private static void SetIPAddress()
    {
        try
        {
            string hostName = Dns.GetHostName();
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

            foreach (IPAddress ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork) // Check for IPv4 addresses
                {
                    iXRAuthentication.IpAddress = ip.ToString();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("iXRLib - Failed to get local IP address: " + ex.Message);
        }
    }
}
