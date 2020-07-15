﻿using System;
using ColossalFramework;
using ColossalFramework.UI;
using System.Collections.Generic;
using UnityEngine;
using Painter;


namespace Repaint
{
    public class Repaint : Singleton<Repaint>
    {
        internal void Colorize(BuildingInfo building, bool invert)
        {
            try
            {
                building.GetComponent<Renderer>().material.UpdateACI(invert);
                building.m_lodObject.GetComponent<Renderer>().material.UpdateACI(invert);
                BuildingInfo.MeshInfo[] subMeshes = building.m_subMeshes;
                foreach (BuildingInfo.MeshInfo meshInfo in subMeshes)
                {
                    try
                    {
                        meshInfo.m_subInfo.GetComponent<Renderer>().material.UpdateACI(invert);
                        meshInfo.m_subInfo.m_lodObject.GetComponent<Renderer>().material.UpdateACI(invert);
                    }
                    catch (Exception message)
                    {
                        Debug.LogWarning(message);
                    }
                }
            }
            catch (Exception message2)
            {
                Debug.LogWarning(message2);
            }
        }

        public PainterColorizer Colorizer
        {
            get
            {
                if (colorizer == null)
                {
                    colorizer = PainterColorizer.Load();
                    if (colorizer == null)
                    {
                        colorizer = new PainterColorizer();
                        colorizer.Save();
                    }
                }
                return colorizer;
            }
            set
            {
                colorizer = value;
            }
        }
        private PainterColorizer colorizer;
        private UICheckBox colorizeCheckbox;
        private UICheckBox invertCheckbox;
        private string ColorizeText => RepaintMod.Translation.GetTranslation("PAINTER-COLORIZE");
        private string InvertText => RepaintMod.Translation.GetTranslation("PAINTER-INVERT");
        private string ReloadRequiredTooltip => RepaintMod.Translation.GetTranslation("PAINTER-RELOAD-REQUIRED");



        private Dictionary<ushort, SerializableColor> colors;
        internal Dictionary<ushort, SerializableColor> Colors
        {
            get
            {
                if (colors == null) colors = new Dictionary<ushort, SerializableColor>();
                return colors;
            }
            set
            {
                colors = value;
            }
        }
        private Dictionary<PanelType, BuildingWorldInfoPanel> Panels;
        internal Dictionary<PanelType, UIColorField> ColorFields;
        private UIColorField colorFIeldTemplate;
        private UIButton copyButton;
        private UIButton resetButton;
        private UIButton pasteButton;
        private Color32 copyPasteColor;
        internal ushort BuildingID;
        internal bool IsPanelVisible;
        internal bool isPickerOpen;
        private string CopyText => RepaintMod.Translation.GetTranslation("PAINTER-COPY");
        private string PasteText => RepaintMod.Translation.GetTranslation("PAINTER-PASTE");
        private string ResetText => RepaintMod.Translation.GetTranslation("PAINTER-RESET");
                
        private void Update()
        {
            if (!isPickerOpen) return;
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) && Input.GetKeyDown(KeyCode.C))
                copyPasteColor = GetColor();
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) && Input.GetKeyDown(KeyCode.V))
                PasteColor();
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) && (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
                EraseColor();    
        }

        internal Color GetColor()
        {
            var building = BuildingManager.instance.m_buildings.m_buffer[BuildingID];
            return building.Info.m_buildingAI.GetColor(BuildingID, ref building, InfoManager.InfoMode.None);
        }

        private void UpdateColor(Color32 color, ushort currentBuilding)
        {
            if (!Colors.TryGetValue(currentBuilding, out SerializableColor col))
                Colors.Add(currentBuilding, color);
            else Colors[currentBuilding] = color;
            BuildingManager.instance.UpdateBuildingColors(currentBuilding);
        }

        private void ResetColor()
        {
            if (Colors.TryGetValue(BuildingID, out SerializableColor color))
                Colors.Remove(BuildingID);
            BuildingManager.instance.UpdateBuildingColors(BuildingID);
        }

        private void EraseColor()
        {
            var field = Panels[PanelType.Service].component.isVisible ? ColorFields[PanelType.Service] : Panels[PanelType.Shelter].component.isVisible ? ColorFields[PanelType.Shelter] : ColorFields[PanelType.Zoned];
            ResetColor();
            field.selectedColor = GetColor();
            field.SendMessage("ClosePopup", false);
            field.SendMessage("OpenPopup");
        }

        private void PasteColor()
        {
            var field = Panels[PanelType.Service].component.isVisible ? ColorFields[PanelType.Service] : Panels[PanelType.Shelter].component.isVisible ? ColorFields[PanelType.Shelter] : ColorFields[PanelType.Zoned];
            UpdateColor(copyPasteColor, BuildingID);
            field.selectedColor = copyPasteColor;
            field.SendMessage("ClosePopup", false);
            field.SendMessage("OpenPopup");
        }

        internal void AddColorFieldsToPanels()
        {
            Panels = new Dictionary<PanelType, BuildingWorldInfoPanel>
            {
                [PanelType.Service] = UIView.library.Get<CityServiceWorldInfoPanel>(typeof(CityServiceWorldInfoPanel).Name),
                [PanelType.Shelter] = UIView.library.Get<ShelterWorldInfoPanel>(typeof(ShelterWorldInfoPanel).Name),
                [PanelType.Zoned] = UIView.library.Get<ZonedBuildingWorldInfoPanel>(typeof(ZonedBuildingWorldInfoPanel).Name)
            };
            ColorFields = new Dictionary<PanelType, UIColorField>
            {
                [PanelType.Service] = CreateColorField(Panels[PanelType.Service]?.component),
                [PanelType.Shelter] = CreateColorField(Panels[PanelType.Shelter]?.component),
                [PanelType.Zoned] = CreateColorField(Panels[PanelType.Zoned]?.component),
            };
        }

        private UIColorField CreateColorField(UIComponent parent)
        {
            if (colorFIeldTemplate == null)
            {
                UIComponent template = UITemplateManager.Get("LineTemplate");
                if (template == null) return null;

                colorFIeldTemplate = template.Find<UIColorField>("LineColor");
                if (colorFIeldTemplate == null) return null;
            }

            UIColorField cF = Instantiate(colorFIeldTemplate.gameObject).GetComponent<UIColorField>();
            parent.AttachUIComponent(cF.gameObject);

            // Find ProblemsPanel relative position to position ColorField correctly.
            // We'll use 43f as a default relative Y in case something doesn't work.
            UIComponent problemsPanel;
            float relativeY = 43f;

            // Player info panels have wrappers, zoned ones don't.
            UIComponent wrapper = parent.Find("Wrapper");
            if (wrapper == null)
            {
                problemsPanel = parent.Find("ProblemsPanel");
            }
            else
            {
                problemsPanel = wrapper.Find("ProblemsPanel");
            }

            try
            {
                // Position ColorField vertically in the middle of the problems panel.  If wrapper panel exists, we need to add its offset as well.
                relativeY = (wrapper == null ? 0 : wrapper.relativePosition.y) + problemsPanel.relativePosition.y + ((problemsPanel.height - 26) / 2);
            }
            catch
            {
                // Don't care; just use default relative Y.
                Debug.Log("Repaint: couldn't find ProblemsPanel relative position.");
            }

            cF.name = "PainterColorField";
            cF.AlignTo(parent, UIAlignAnchor.TopRight);
            cF.relativePosition += new Vector3(-40f, relativeY, 0f);
            cF.size = new Vector2(26f, 26f);
            cF.pickerPosition = UIColorField.ColorPickerPosition.RightBelow;
            cF.eventSelectedColorChanged += EventSelectedColorChangedHandler;
            cF.eventColorPickerOpen += EventColorPickerOpenHandler;
            cF.eventColorPickerClose += EventColorPickerCloseHandler;
            return cF;
        }


        private UIButton CreateButton(UIComponent parentComponent, string text)
        {
            UIButton button = parentComponent.AddUIComponent<UIButton>();
            button.name = text + "Button";
            button.text = text == "Copy" ? CopyText : text == "Paste" ? PasteText : ResetText;
            button.width = 71.33333333333333f;
            button.height = 20f;
            button.textPadding = new RectOffset(0, 0, 5, 0);
            button.horizontalAlignment = UIHorizontalAlignment.Center;
            button.textVerticalAlignment = UIVerticalAlignment.Middle;
            button.textScale = 0.8f;
            button.atlas = UIView.GetAView().defaultAtlas;
            button.normalBgSprite = "ButtonMenu";
            button.disabledBgSprite = "ButtonMenuDisabled";
            button.hoveredBgSprite = "ButtonMenuHovered";
            button.focusedBgSprite = "ButtonMenu";
            button.pressedBgSprite = "ButtonMenuPressed";            
            return button;
        }

        private void EventSelectedColorChangedHandler(UIComponent component, Color value)
        {
            UpdateColor(value, BuildingID);
        }

        private void EventColorPickerOpenHandler(UIColorField colorField, UIColorPicker colorPicker, ref bool overridden)
        {
            colorPicker.component.height += 60f;
            if (colorizeCheckbox == null)
            {
                colorizeCheckbox = CreateCheckBox(colorPicker.component, "Colorize");
            }
            if (invertCheckbox == null)
            {
                invertCheckbox = CreateCheckBox(colorPicker.component, "Invert");
            }


            // Set visibility flag.
            isPickerOpen = true;

            colorPicker.component.height += 30f;
            copyButton = CreateButton(colorPicker.component, "Copy");
            pasteButton = CreateButton(colorPicker.component, "Paste");
            resetButton = CreateButton(colorPicker.component, "Reset");
            copyButton.relativePosition = new Vector3(10f, 223f);
            pasteButton.relativePosition = new Vector3(91.33333333333333f, 223f);
            resetButton.relativePosition = new Vector3(172.6666666666667f, 223f);

            colorizeCheckbox.relativePosition = new Vector3(10f, 253f);
            invertCheckbox.relativePosition = new Vector3(127f, 253f);

            copyButton.eventClick += (c, e) =>
            {
                copyPasteColor = GetColor();
            };
            pasteButton.eventClick += (c, e) =>
            {
                PasteColor();
            };
            resetButton.eventClick += (c, e) =>
            {
                EraseColor();
            };
        }


        /// <summary>
        /// Called when color picker is closed.
        /// </summary>
        /// <param name="colorField">Ignored</param>
        /// <param name="colorPicker">Ignored</param>
        /// <param name="overridden">Ignored</param>
        private void EventColorPickerCloseHandler(UIColorField colorField, UIColorPicker colorPicker, ref bool overridden)
        {
            // Set visibility flag.
            isPickerOpen = false;
        }



        public UICheckBox CreateCheckBox(UIComponent parent, string fieldName)
        {
            UICheckBox uICheckBox = parent.AddUIComponent<UICheckBox>();
            uICheckBox.name = fieldName;
            uICheckBox.width = 20f;
            uICheckBox.height = 20f;
            uICheckBox.relativePosition = Vector3.zero;
            UILabel uILabel = uICheckBox.AddUIComponent<UILabel>();
            uILabel.text = ((fieldName == "Colorize") ? ColorizeText : InvertText);
            uILabel.textScale = 0.8f;
            uILabel.relativePosition = new Vector3(22f, 5f);
            UISprite uISprite = uICheckBox.AddUIComponent<UISprite>();
            uISprite.spriteName = "ToggleBase";
            uISprite.size = new Vector2(16f, 16f);
            uISprite.relativePosition = new Vector3(2f, 2f);
            uICheckBox.checkedBoxObject = uISprite.AddUIComponent<UISprite>();
            ((UISprite)uICheckBox.checkedBoxObject).spriteName = "ToggleBaseFocused";
            uICheckBox.checkedBoxObject.size = new Vector2(16f, 16f);
            uICheckBox.checkedBoxObject.relativePosition = Vector3.zero;
            string name = Singleton<BuildingManager>.instance.m_buildings.m_buffer[BuildingID].Info.name;
            uICheckBox.isChecked = ((fieldName == "Colorize") ? Colorizer.Colorized.Contains(name) : Colorizer.Inverted.Contains(name));
            uICheckBox.tooltip = ReloadRequiredTooltip;
            return uICheckBox;
        }

    }

    public enum PanelType
    {
        None = -1,
        Service,
        Shelter,
        Zoned,
        Count
    }
}
