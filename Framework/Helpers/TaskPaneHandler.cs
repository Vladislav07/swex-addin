﻿//**********************
//SwEx.AddIn - development tools for SOLIDWORKS add-ins
//Copyright(C) 2019 www.codestack.net
//License: https://github.com/codestackdev/swex-addin/blob/master/LICENSE
//Product URL: https://www.codestack.net/labs/solidworks/swex/add-in/
//**********************

using CodeStack.SwEx.AddIn.Attributes;
using CodeStack.SwEx.AddIn.Icons;
using CodeStack.SwEx.Common.Base;
using CodeStack.SwEx.Common.Diagnostics;
using CodeStack.SwEx.Common.Icons;
using CodeStack.SwEx.Common.Reflection;
using SolidWorks.Interop.sldworks;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace CodeStack.SwEx.AddIn.Helpers
{
    internal interface ITaskPaneHandler : IDisposable
    {
        event Action<ITaskPaneHandler> Disposed;
        void Delete();
    }

    internal enum EmptyTaskPaneCommands_e
    {
    }

    internal class TaskPaneHandler<TCmdEnum> : ITaskPaneHandler
        where TCmdEnum : IComparable, IFormattable, IConvertible
    {
        public event Action<ITaskPaneHandler> Disposed;

        private const int S_OK = 0;

        private readonly ITaskpaneView m_TaskPaneView;
        private readonly ILogger m_Logger;

        private readonly Action<TCmdEnum> m_CmdHandler;
        private readonly TCmdEnum[] m_Commands;

        internal TaskPaneHandler(ISldWorks app, ITaskpaneView taskPaneView,
            Action<TCmdEnum> cmdHandler, IIconsConverter iconsConv, ILogger logger)
        {
            m_Logger = logger;
            
            m_TaskPaneView = taskPaneView;
            m_CmdHandler = cmdHandler;

            if (!typeof(TCmdEnum).IsEnum)
            {
                throw new ArgumentException($"{typeof(TCmdEnum)} must be an enumeration");
            }

            if (typeof(TCmdEnum) != typeof(EmptyTaskPaneCommands_e) && cmdHandler != null)
            {
                var enumValues = Enum.GetValues(typeof(TCmdEnum));

                m_Commands = enumValues.Cast<TCmdEnum>().ToArray();

                foreach (Enum cmdEnum in enumValues)
                {
                    //NOTE: unlike task pane icon, command icons must have the same transparency key as command manager commands
                    var icon = DisplayInfoExtractor.ExtractCommandDisplayIcon<CommandIconAttribute, CommandGroupIcon>(
                        cmdEnum,
                        i => new MasterIcon(i),
                        a => a.Icon);

                    var tooltip = "";

                    if (!cmdEnum.TryGetAttribute<DisplayNameAttribute>(a => tooltip = a.DisplayName))
                    {
                        cmdEnum.TryGetAttribute<DescriptionAttribute>(a => tooltip = a.Description);
                    }

                    if (!cmdEnum.TryGetAttribute<TaskPaneStandardButtonAttribute>(a => 
                    {
                        if (!m_TaskPaneView.AddStandardButton((int)a.Icon, tooltip))
                        {
                            throw new InvalidOperationException($"Failed to add standard button for {cmdEnum}");
                        }
                    }))
                    {
                        if (app.SupportsHighResIcons(SldWorksExtension.HighResIconsScope_e.TaskPane))
                        {
                            var imageList = iconsConv.ConvertIcon(icon, true);
                            if (!m_TaskPaneView.AddCustomButton2(imageList, tooltip))
                            {
                                throw new InvalidOperationException($"Failed to create task pane button for {cmdEnum} with highres icon");
                            }
                        }
                        else
                        {
                            var imagePath = iconsConv.ConvertIcon(icon, false)[0];
                            if (!m_TaskPaneView.AddCustomButton(imagePath, tooltip))
                            {
                                throw new InvalidOperationException($"Failed to create task pane button for {cmdEnum}");
                            }
                        }
                    }
                }

                (m_TaskPaneView as TaskpaneView).TaskPaneToolbarButtonClicked += OnTaskPaneToolbarButtonClicked;
            }

            (m_TaskPaneView as TaskpaneView).TaskPaneDestroyNotify += OnTaskPaneDestroyNotify;
        }

        private int OnTaskPaneToolbarButtonClicked(int buttonIndex)
        {
            m_Logger.Log($"Task pane button clicked: {buttonIndex}");

            if (m_Commands?.Length > buttonIndex)
            {
                m_CmdHandler.Invoke(m_Commands[buttonIndex]);
            }
            else
            {
                m_Logger.Log($"Invalid task pane button id is clicked: {buttonIndex}");
                Debug.Assert(false, "Invalid command id");
            }

            return S_OK;
        }

        private int OnTaskPaneDestroyNotify()
        {
            m_Logger.Log("Destroying task pane");

            Dispose();
            return S_OK;
        }

        public void Delete()
        {
            if (!m_TaskPaneView.DeleteView())
            {
                throw new InvalidOperationException("Failed to remove TaskPane");
            }
        }

        public void Dispose()
        {
            (m_TaskPaneView as TaskpaneView).TaskPaneDestroyNotify -= OnTaskPaneDestroyNotify;
            (m_TaskPaneView as TaskpaneView).TaskPaneToolbarButtonClicked -= OnTaskPaneToolbarButtonClicked;

            var ctrl = m_TaskPaneView.GetControl();

            if (ctrl is IDisposable)
            {
                (ctrl as IDisposable).Dispose();
            }

            Disposed?.Invoke(this);
        }
    }
}
