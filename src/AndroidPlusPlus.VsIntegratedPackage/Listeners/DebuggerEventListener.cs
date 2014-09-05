﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using EnvDTE;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell.Interop;

using AndroidPlusPlus.Common;
using AndroidPlusPlus.VsDebugEngine;

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace AndroidPlusPlus.VsIntegratedPackage
{

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  public delegate int DebuggerEventListenerDelegate (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  interface DebuggerEventListenerInterface
  {
    int OnSessionCreate (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

    int OnSessionDestroy (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

    int OnEngineCreate (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

    int OnProgramCreate (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

    int OnProgramDestroy (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

    int OnAttachComplete (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

    int OnError (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);

    int OnUiDebugLaunchServiceEvent (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib);
  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  [ComVisible (false)]

  public class DebuggerEventListener : DebuggerEventListenerInterface, IVsDebuggerEvents, IDebugEventCallback2
  {

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private readonly DTE m_dteService;

    private readonly IVsDebugger m_debuggerService;

    private readonly IUiDebugLaunchService m_debugLaunchService;

    private uint m_debuggerServiceCookie;

    private Dictionary<Guid, DebuggerEventListenerDelegate> m_eventCallbacks;

    private AsyncRedirectProcess m_adbLogcatInstance;

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public DebuggerEventListener (DTE dteService, IVsDebugger debuggerService, IUiDebugLaunchService debugLaunchService)
    {
      m_dteService = dteService;

      m_debuggerService = debuggerService;

      m_debugLaunchService = debugLaunchService;

      LoggingUtils.RequireOk (m_debuggerService.AdviseDebuggerEvents (this, out m_debuggerServiceCookie));

      LoggingUtils.RequireOk (m_debuggerService.AdviseDebugEventCallback (this));

      // 
      // Register required listener events and paired process function callbacks.
      // 

      m_eventCallbacks = new Dictionary<Guid, DebuggerEventListenerDelegate> ();

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.SessionCreate)), OnSessionCreate);

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.SessionDestroy)), OnSessionDestroy);

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.EngineCreate)), OnEngineCreate);

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.ProgramCreate)), OnProgramCreate);

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.ProgramDestroy)), OnProgramDestroy);

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.AttachComplete)), OnAttachComplete);

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.Error)), OnError);

      m_eventCallbacks.Add (ComUtils.GuidOf (typeof (DebugEngineEvent.UiDebugLaunchServiceEvent)), OnUiDebugLaunchServiceEvent);
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    private class DeviceLogcatListener : AsyncRedirectProcess.EventListener
    {
      public void ProcessStdout (object sendingProcess, DataReceivedEventArgs args)
      {
        if (!string.IsNullOrWhiteSpace (args.Data))
        {
          UiOutputWindow.WriteLine (VSConstants.OutputWindowPaneGuid.DebugPane_guid, args.Data);
        }
      }

      public void ProcessStderr (object sendingProcess, DataReceivedEventArgs args)
      {
        if (!string.IsNullOrWhiteSpace (args.Data))
        {
          UiOutputWindow.WriteLine (VSConstants.OutputWindowPaneGuid.DebugPane_guid, args.Data);
        }
      }

      public void ProcessExited (object sendingProcess, EventArgs args)
      {
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IVsDebuggerEvents Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnModeChange (DBGMODE dbgmodeNew)
    {
      LoggingUtils.Print ("[DebuggerEventListener] OnModeChange: " + dbgmodeNew.ToString ());

      switch (dbgmodeNew)
      {
        case DBGMODE.DBGMODE_Design:
        case DBGMODE.DBGMODE_Break:
        case DBGMODE.DBGMODE_Run:
        {
          break;
        }
      }

      return VSConstants.S_OK;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region IDebugEventCallback2 Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int Event (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      try
      {
        DebuggerEventListenerDelegate callback;

        LoggingUtils.Print ("[DebuggerEventListener] Event: " + riidEvent.ToString ());

        if (!m_eventCallbacks.TryGetValue (riidEvent, out callback))
        {
          return DebugEngineConstants.E_NOTIMPL;
        }

        int handle = callback (pEngine, pProcess, pProgram, pThread, pEvent, ref riidEvent, dwAttrib);

        if (handle != DebugEngineConstants.E_NOTIMPL)
        {
          LoggingUtils.RequireOk (handle);
        }

        return VSConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return VSConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #region DebuggerEventListenerInterface Members

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnSessionCreate (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      return VSConstants.E_NOTIMPL;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnSessionDestroy (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      return VSConstants.E_NOTIMPL;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnEngineCreate (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      return VSConstants.E_NOTIMPL;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnProgramCreate (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      try
      {
        /*DebuggeeProgram p = pProgram as DebuggeeProgram;

        bool worked = false;

        if (pProgram is DebuggeeProgram)
        {
          worked = true;
        }

        Type t = pProgram.GetType ();

        IntPtr pUnk = Marshal.GetIUnknownForObject (pProgram);

        Guid guidDebuggeeProgram = ComUtils.GuidOf (typeof (IDebugProgram2));

        IDebugProgram2 debuggeeProgramObj = (IDebugProgram2) Marshal.GetTypedObjectForIUnknown (pUnk, typeof (IDebugProgram2));

        DebuggeeProgram debuggeeProgram = debuggeeProgramObj as DebuggeeProgram;

        debuggeeProgram = (DebuggeeProgram) Marshal.CreateWrapperOfType (debuggeeProgramObj, typeof (IDebugProgram2));
        */

        m_adbLogcatInstance = AndroidAdb.GetConnectedDevices () [0].Logcat (new DeviceLogcatListener (), true);

        return VSConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return VSConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnProgramDestroy (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      try
      {
        if (m_adbLogcatInstance != null)
        {
          m_adbLogcatInstance.Kill ();

          m_adbLogcatInstance.Dispose ();

          m_adbLogcatInstance = null;
        }

        return VSConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return VSConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnAttachComplete (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      return VSConstants.E_NOTIMPL;
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnError (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      try
      {
        DebugEngineEvent.Error errorEvent = pEvent as DebugEngineEvent.Error;

        enum_MESSAGETYPE [] messageType = new enum_MESSAGETYPE [1];

        string errorFormat, errorHelpFileName;

        int errorReason;

        uint errorType, errorHelpId;

        LoggingUtils.RequireOk (errorEvent.GetErrorMessage (messageType, out errorFormat, out errorReason, out errorType, out errorHelpFileName, out errorHelpId));

        LoggingUtils.RequireOk (m_debugLaunchService.LaunchDialogUpdate (errorFormat, true));

        return VSConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return VSConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public int OnUiDebugLaunchServiceEvent (IDebugEngine2 pEngine, IDebugProcess2 pProcess, IDebugProgram2 pProgram, IDebugThread2 pThread, IDebugEvent2 pEvent, ref Guid riidEvent, uint dwAttrib)
    {
      LoggingUtils.PrintFunction ();

      try
      {
        DebugEngineEvent.UiDebugLaunchServiceEvent launchServiceEvent = pEvent as DebugEngineEvent.UiDebugLaunchServiceEvent;

        switch (launchServiceEvent.Type)
        {
          case DebugEngineEvent.UiDebugLaunchServiceEvent.EventType.ShowDialog:
          {
            LoggingUtils.RequireOk (m_debugLaunchService.LaunchDialogShow ());

            break;
          }

          case DebugEngineEvent.UiDebugLaunchServiceEvent.EventType.CloseDialog:
          {
            LoggingUtils.RequireOk (m_debugLaunchService.LaunchDialogClose ());

            break;
          }

          case DebugEngineEvent.UiDebugLaunchServiceEvent.EventType.LogStatus:
          case DebugEngineEvent.UiDebugLaunchServiceEvent.EventType.LogError:
          {
            LoggingUtils.RequireOk (m_debugLaunchService.LaunchDialogUpdate (launchServiceEvent.Message, (launchServiceEvent.Type == DebugEngineEvent.UiDebugLaunchServiceEvent.EventType.LogError)));

            break;
          }
        }

        return VSConstants.S_OK;
      }
      catch (Exception e)
      {
        LoggingUtils.HandleException (e);

        return VSConstants.E_FAIL;
      }
    }

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    #endregion

    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

  }

  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
  ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

}

////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
