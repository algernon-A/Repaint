﻿using System;
using System.Collections.Generic;
using UnityEngine;
using ColossalFramework;
using ColossalFramework.UI;
using ColossalFramework.Globalization;
using Painter;


namespace Repaint
{
    /// <summary>
    /// Reference indexes to the three types of info panels (service, shelter, zoned).
    /// </summary>
    public enum PanelType
    {
        None = -1,
        Service,
        Shelter,
        Zoned,
        Warehouse,
        Stadium,
        VarsityStadium,
        Count
    }


    /// <summary>
    /// Main mod class - implements UI and applies colour settings.
    /// </summary>
    public class Repaint : Singleton<Repaint>
    {
        // Layout constants.
        private const float TextFieldY = 220f;
        private const float ButtonY = TextFieldY + 27f;
        private const float CheckBoxY = ButtonY + 27f;
        private const float ControlsHeight = CheckBoxY - TextFieldY + 25f;
        private const float ColumnWidth = 244f / 3f;
        private const float ButtonWidth = ColumnWidth - 10f;
        private const float TextFieldWidth = 50f;
        private const float TextFieldOffset = ButtonWidth - TextFieldWidth;
        private const float Column1X = 10f;
        private const float Column2X = Column1X + ColumnWidth;
        private const float Column3X = Column2X + ColumnWidth;

        // UI Components - base functionality.
        private Dictionary<PanelType, BuildingWorldInfoPanel> Panels;
        internal Dictionary<PanelType, UIColorField> ColorFields;
        private UIColorField colorFieldTemplate;
        private UIColorPicker picker;
        private UIButton copyButton, resetButton, pasteButton;
        private UITextField redField, blueField, greenField;

        // UI Components - colorizer.
        private PainterColorizer colorizer;
        private UICheckBox colorizeCheckbox, invertCheckbox;

        // Variables and flags.
        private Color32 copyPasteColor;
        internal ushort BuildingID;
        internal bool IsPanelVisible, isPickerOpen, suspendEvents;
        private float currentRed, currentGreen, currentBlue;


        /// <summary>
        /// Dictionary of building colour settings.
        /// Key mod data record.
        /// </summary>
        internal Dictionary<ushort, SerializableColor> Colors
        {
            get
            {
                if (_colors == null)
                {
                    _colors = new Dictionary<ushort, SerializableColor>();
                }
                return _colors;
            }
            set
            {
                _colors = value;
            }
        }
        private Dictionary<ushort, SerializableColor> _colors;


        /// <summary>
        /// Loads and saves colorizer file settings.
        /// </summary>
        public PainterColorizer Colorizer
        {
            get
            {
                // Check to see that we haven't already done this.
                if (colorizer == null)
                {
                    // Attempt to load colorizer data from file.
                    colorizer = PainterColorizer.Load();

                    // If we failed, create a new colorizer and save it.
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


        /// <summary>
        /// Unity update method to check and handle keystroke inputs for the color picker when it's open.
        /// </summary>
        public void Update()
        {
            // Only interested when the picker is open.
            if (isPickerOpen)
            {
                // Check for appropriate modifier keys.
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand))
                {
                    // Copy key.
                    if (Input.GetKeyDown(KeyCode.C))
                    {
                        // Copy the current colour selection.
                        copyPasteColor = GetColor();
                    }
                    // Paste key.
                    else if (Input.GetKeyDown(KeyCode.V))
                    {
                        // Paste the copied colour.
                        PasteColor();
                    }
                    // Erase key.
                    if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
                    {
                        // Erase custom colour setting.
                        EraseColor();
                    }
                }
            }
        }


        /// <summary>
        /// Colorizes or inverts a building.
        /// Part of the colorization function.
        /// </summary>
        /// <param name="building">Building prefab to colorize/invert</param>
        /// <param name="invert">True to invert, false otherwise</param>
        internal void Colorize(BuildingInfo building, bool invert)
        {
            if (building?.name != null)
            {
                try
                {
                    // Apply ACI changes to main mesh.
                    building.GetComponent<Renderer>()?.material?.UpdateACI(invert, false);

                    // Ditto for buildings with custom renderers.
                    building.m_overrideMainRenderer?.material?.UpdateACI(invert, false);

                    // Ditto for LODs.
                    building.m_lodObject?.GetComponent<Renderer>()?.material?.UpdateACI(invert, true);

                    // Any submeshes?
                    if (building.m_subMeshes != null && building.m_subMeshes.Length > 0)
                    {
                        // Yes -iterate through each submeshe and apply settings as well.
                        for (int i = 0; i < building.m_subMeshes.Length; i++)
                        {
                            if (building.m_subMeshes[i].m_subInfo is BuildingInfoSub subInfo)
                            {
                                try
                                {
                                    // Apply ACI changes to main mesh and LOD, where they exist.
                                    subInfo.GetComponent<Renderer>()?.material?.UpdateACI(invert, false);
                                    subInfo.m_lodObject?.GetComponent<Renderer>()?.material?.UpdateACI(invert, true);
                                }
                                catch (Exception e)
                                {
                                    // Don't let a single failure stop the whole process.
                                    Logging.LogException(e, "exception colorizing sub-mesh for building ", building.name);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // Don't let a single failure kill the mod.
                    Logging.LogException(e, "exception colorizing building ", building.name);
                }
            }
        }


        /// <summary>
        /// Gets the current building's colour setting.
        /// </summary>
        /// <returns>Current building's colour</returns>
        internal Color GetColor()
        {
            // Get current building instance.
            Building building = BuildingManager.instance.m_buildings.m_buffer[BuildingID];

            // Lookup and return the set colour.
            return building.Info.m_buildingAI.GetColor(BuildingID, ref building, InfoManager.InfoMode.None);
        }


        /// <summary>
        /// Add color fields to building info panels.
        /// </summary>
        internal void AddColorFieldsToPanels()
        {
            // Create new panel dictionary and populate with relevant game info panel references.
            Panels = new Dictionary<PanelType, BuildingWorldInfoPanel>
            {
                [PanelType.Service] = UIView.library.Get<CityServiceWorldInfoPanel>(typeof(CityServiceWorldInfoPanel).Name),
                [PanelType.Shelter] = UIView.library.Get<ShelterWorldInfoPanel>(typeof(ShelterWorldInfoPanel).Name),
                [PanelType.Zoned] = UIView.library.Get<ZonedBuildingWorldInfoPanel>(typeof(ZonedBuildingWorldInfoPanel).Name),
                [PanelType.Warehouse] = UIView.library.Get<WarehouseWorldInfoPanel>(typeof(WarehouseWorldInfoPanel).Name),
                [PanelType.Stadium] = UIView.library.Get<FootballPanel>(typeof(FootballPanel).Name),
                [PanelType.VarsityStadium] = UIView.library.Get<VarsitySportsArenaPanel>(typeof(VarsitySportsArenaPanel).Name)
            };

            // Create new color field dictionary and populate with new color fields.
            ColorFields = new Dictionary<PanelType, UIColorField>
            {
                [PanelType.Service] = CreateColorField(Panels[PanelType.Service]?.component),
                [PanelType.Shelter] = CreateColorField(Panels[PanelType.Shelter]?.component),
                [PanelType.Zoned] = CreateColorField(Panels[PanelType.Zoned]?.component),
                [PanelType.Warehouse] = CreateColorField(Panels[PanelType.Warehouse]?.component),
                [PanelType.Stadium] = CreateColorField(Panels[PanelType.Stadium]?.component),
                [PanelType.VarsityStadium] = CreateColorField(Panels[PanelType.VarsityStadium]?.component)
            };
        }


        /// <summary>
        /// Update a building's colour setting.
        /// </summary>
        /// <param name="color">Colour to apply</param>
        /// <param name="currentBuilding">Building to apply to</param>
        private void UpdateColor(Color32 color, ushort currentBuilding)
        {
            // See if we already have a setting for this building.
            if (Colors.ContainsKey(currentBuilding))
            {
                // Yes - update current setting.
                Colors[currentBuilding] = color;
            }
            else
            {
                // No existing setting - add a new one.
                Colors.Add(currentBuilding, color);
            }

            // Apply colour change in-game via the building manager.
            BuildingManager.instance.UpdateBuildingColors(BuildingID);
        }


        /// <summary>
        /// Removes the current building's custom colour setting.
        /// </summary>
        private void ResetColor()
        {
            // Check that we already have a setting to remove.
            if (Colors.ContainsKey(BuildingID))
            {
                // Remove the setting.
                Colors.Remove(BuildingID);

                // Apply colour change in-game via the building manager.
                BuildingManager.instance.UpdateBuildingColors(BuildingID);
            }
        }


        /// <summary>
        /// Returns the currently displayed color field.
        /// </summary>
        private UIColorField CurrentColorField =>
            Panels[PanelType.Service].component.isVisible ? ColorFields[PanelType.Service] :
            Panels[PanelType.Shelter].component.isVisible ? ColorFields[PanelType.Shelter] :
            Panels[PanelType.Zoned].component.isVisible ? ColorFields[PanelType.Zoned] :
            Panels[PanelType.Warehouse].component.isVisible ? ColorFields[PanelType.Warehouse] :
            Panels[PanelType.Stadium].component.isVisible ? ColorFields[PanelType.Stadium] :
            ColorFields[PanelType.Stadium];


        /// <summary>
        /// Erases the custom colour selection for a building, as directed by the user.
        /// </summary>
        private void EraseColor()
        {
            // Get the current color field.
            UIColorField field = CurrentColorField;

            // Remove custom colour from building.
            ResetColor();

            // Reset the color field to the building's "natural" colour.
            field.selectedColor = GetColor();

            // Close and re-open the popup (to refresh).
            field.SendMessage("ClosePopup", false);
            field.SendMessage("OpenPopup");
        }


        /// <summary>
        /// Pastes a custom colour selection to the selected building, as directed by the user.
        /// </summary>
        private void PasteColor()
        {
            // Get the current color field.
            UIColorField field = CurrentColorField;

            // Update building's colour to the copied value.
            UpdateColor(copyPasteColor, BuildingID);

            // Reset the color field to the building's "natural" colour.
            field.selectedColor = copyPasteColor;

            // Close and re-open the popup (to refresh).
            field.SendMessage("ClosePopup", false);
            field.SendMessage("OpenPopup");
        }


        /// <summary>
        /// Create a new color field for a building info panel.
        /// </summary>
        /// <param name="parent">Parent component</param>
        /// <returns>New color field</returns>
        private UIColorField CreateColorField(UIComponent parent)
        {
            const float ColorFieldSize = 26f;

            // Component to position the field against.
            UIComponent problemsPanel;


            // Create the color field template if we haven't already.
            if (colorFieldTemplate == null)
            {
                // Get line template, and return null if we fail.
                UIComponent template = UITemplateManager.Get("LineTemplate");
                if (template == null)
                {
                    Logging.Message("failed to get LineTemplate");
                    return null;
                }

                // Get colour template, and return null if we fail.
                colorFieldTemplate = template.Find<UIColorField>("LineColor");
                if (colorFieldTemplate == null)
                {
                    Logging.Message("failed to get LineColor template");
                    return null;
                }
            }

            // Intantiate our template as a new color field and attach to our parent.
            UIColorField colorField = Instantiate(colorFieldTemplate.gameObject).GetComponent<UIColorField>();
            parent.AttachUIComponent(colorField.gameObject);

            // Find ProblemsPanel relative position to position ColorField correctly.
            // We'll use 43f as a default relative Y in case something doesn't work.
            float relativeY = 43f;

            // Player info panels have wrappers, zoned ones don't.
            UIComponent wrapper = parent.Find("Wrapper");

            if (wrapper == null)
            {
                problemsPanel = parent.Find("ProblemsPanel");
            }
            else
            {
                // Varisty sports have ProblemsLayoutPanel in between wrapper and problemsPanel.
                UIComponent problemsLayout = wrapper.Find("ProblemsLayoutPanel");

                if (problemsLayout != null)
                {
                    wrapper = problemsLayout;
                }

                problemsPanel = wrapper.Find("ProblemsPanel");
            }

            try
            {
                // Position ColorField vertically in the middle of the problems panel.  If wrapper panel exists, we need to add its offset as well.
                relativeY = (wrapper == null ? 0 : wrapper.relativePosition.y) + problemsPanel.relativePosition.y + ((problemsPanel.height - ColorFieldSize) / 2);
            }
            catch
            {
                // Don't care; just use default relative Y.
                Logging.Error("couldn't find ProblemsPanel relative position");
            }

            // Set up the new color field.
            colorField.name = "RepaintColorField";
            colorField.AlignTo(parent, UIAlignAnchor.TopLeft);
            colorField.relativePosition += new Vector3(parent.width - 40f - ColorFieldSize, relativeY, 0f);
            colorField.size = new Vector2(ColorFieldSize, ColorFieldSize);
            colorField.pickerPosition = UIColorField.ColorPickerPosition.RightBelow;

            // Event handlers.
            colorField.eventSelectedColorChanged += EventSelectedColorChangedHandler;
            colorField.eventColorPickerOpen += EventColorPickerOpenHandler;
            colorField.eventColorPickerClose += EventColorPickerCloseHandler;

            return colorField;
        }


        /// <summary>
        /// Event handler - called when a new colour is selected.
        /// </summary>
        /// <param name="component">Calling component</param>
        /// <param name="value">New colour</param>
        private void EventSelectedColorChangedHandler(UIComponent component, Color value)
        {
            UpdateColor(value, BuildingID);

            // Don't update color references if events are suspended (to avoid circular overwrite of values when ColorPicker color is changed due to RGB textfield entry).
            if (!suspendEvents)
            {
                // Update color references - we use these to avoid cumulative rounding and adjusment errors that can occur when using the color picker color directly.
                currentRed = value.r;
                currentGreen = value.g;
                currentBlue = value.b;
            }
            
            UpdateTextFields();
        }


        /// <summary>
        /// Event handler - opens the color picker.
        /// </summary>
        /// <param name="colorField">Calling color field</param>
        /// <param name="colorPicker">Color picker to open</param>
        /// <param name="overridden"></param>
        private void EventColorPickerOpenHandler(UIColorField colorField, UIColorPicker colorPicker, ref bool overridden)
        {
            // Set reference.
            picker = colorPicker;

            // Increase the colour picker component height to accomodate controls below.
            colorPicker.component.height += ControlsHeight;

            // Create copy, paste, and reset buttons.
            copyButton = CreateButton(colorPicker.component, Translations.Translate("PAINTER-COPY"), Column1X);
            pasteButton = CreateButton(colorPicker.component, Translations.Translate("PAINTER-PASTE"), Column2X);
            resetButton = CreateButton(colorPicker.component, Translations.Translate("PAINTER-RESET"), Column3X);

            // Create colorize and invert checkboxes.
            string prefabName = Singleton<BuildingManager>.instance.m_buildings.m_buffer[BuildingID].Info.name;
            colorizeCheckbox = CreateCheckBox(colorPicker.component, Translations.Translate("PAINTER-COLORIZE"), 10f);
            invertCheckbox = CreateCheckBox(colorPicker.component, Translations.Translate("PAINTER-INVERT"), 127f);
            colorizeCheckbox.isChecked = Colorizer.Colorized.Contains(prefabName);
            invertCheckbox.isChecked = Colorizer.Inverted.Contains(prefabName);

            // Create RGB textfields.
            redField = CreateTextField(colorPicker.component, "PAINTER-R", Column1X + TextFieldOffset, "PAINTER-RED");
            greenField = CreateTextField(colorPicker.component, "PAINTER-G", Column2X + TextFieldOffset, "PAINTER-GREEN");
            blueField = CreateTextField(colorPicker.component, "PAINTER-B", Column3X + TextFieldOffset, "PAINTER-BLUE");

            // Record current colors - we use these to avoid cumulative rounding and adjusment errors that can occur when using the color picker color directly.
            currentRed = colorPicker.color.r;
            currentBlue = colorPicker.color.g;
            currentGreen = colorPicker.color.b;

            // Set initial text.
            UpdateTextFields();

            // Set visibility flag.
            isPickerOpen = true;

            // Event handlers - copy, paste, reset.
            copyButton.eventClick += (comppnent, clickEvent) =>
            {
                copyPasteColor = GetColor();
            };
            pasteButton.eventClick += (comppnent, clickEvent) =>
            {
                PasteColor();
            };
            resetButton.eventClick += (comppnent, clickEvent) =>
            {
                EraseColor();
            };

            // Event handler - colorize.
            colorizeCheckbox.eventCheckChanged += (component, isChecked) =>
            {
                // Update list of colorized buildings accordingly.
                HandleCheckboxes(Colorizer.Colorized, isChecked);

                // If we're checked, deselect the invert checkbox.
                if (isChecked)
                {
                    invertCheckbox.isChecked = !isChecked;
                }

                // Save settings.
                Colorizer.Save();
            };

            // Event handler - invert.
            invertCheckbox.eventCheckChanged += (component, isChecked) =>
            {
                // Update list of inverted buildings accordingly.
                HandleCheckboxes(Colorizer.Inverted, isChecked);

                // If we're checked, deselect the colorized checkbox.
                if (isChecked)
                {
                    colorizeCheckbox.isChecked = !isChecked;
                }

                // Save settings.
                Colorizer.Save();
            };

            // Event handlers - textboxes.
            redField.eventTextSubmitted += (component, text) => ParseRGB(redField, ref currentRed);
            greenField.eventTextSubmitted += (component, text) => ParseRGB(greenField, ref currentGreen);
            blueField.eventTextSubmitted += (component, text) => ParseRGB(blueField, ref currentBlue);

            // Event handlers - textboxes.
            redField.eventKeyDown += (component, keyEvent) => TextEnter(keyEvent, ref currentRed, redField, greenField);
            greenField.eventKeyDown += (component, keyEvent) => TextEnter(keyEvent, ref currentGreen, greenField, blueField);
            blueField.eventKeyDown += (component, keyEvent) => TextEnter(keyEvent, ref currentBlue, blueField, redField);

            /* Leftover from development experiments - no longer going this route, but leaving here for now for future reference as it could be quite handy.
             * 
            MethodInfo gameKeyDown = colorField.GetType().GetMethod("OnPopupKeyDown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (gameKeyDown == null)
            {
                Debugging.Message("unable to get OnPopupKeyDown method");
            }
            else
            {
                //colorPicker.component.eventKeyDown -= Delegate.CreateDelegate(typeof(KeyPressHandler), colorField, gameKeyDown) as ColossalFramework.UI.KeyPressHandler;
                //colorPicker.component.eventKeyDown += (control, keyEvent) => HandleKeyDown(keyEvent, colorField, colorPicker);
            }
            */
        }


        /// <summary>
        /// Event handler - called when color picker is closed.
        /// </summary>
        /// <param name="colorField">Ignored</param>
        /// <param name="colorPicker">Ignored</param>
        /// <param name="overridden">Ignored</param>
        private void EventColorPickerCloseHandler(UIColorField colorField, UIColorPicker colorPicker, ref bool overridden)
        {
            // Set visibility flag.
            isPickerOpen = false;
        }


        /// <summary>
        /// Handles colorized/inverted checkbox state changes, adding/removing entries from lists as appropriate.
        /// </summary>
        /// <param name="list">Relevant list of prefabs</param>
        /// <param name="isChecked">Checkbox state</param>
        private void HandleCheckboxes(List<string> list, bool isChecked)
        {
            // Local references.
            BuildingInfo prefab = Singleton<BuildingManager>.instance.m_buildings.m_buffer[BuildingID].Info;
            string buildingName = prefab.name;
            BuildingInfo.SubInfo[] subBuildings = prefab.m_subBuildings;

            // Add/remove main building to relevant list.
            ColorizeListEntry(buildingName, list, isChecked);

            // Sub-buildings.
            if (subBuildings != null && subBuildings.Length > 0)
            {
                foreach (BuildingInfo.SubInfo subBuilding in subBuildings)
                {
                    buildingName = subBuilding?.m_buildingInfo?.name;
                    if (buildingName != null)
                    {
                        ColorizeListEntry(buildingName, list, isChecked);
                    }
                }
            }
        }



        /// <summary>
        /// Adds or removes a building from the provided colorization list.
        /// </summary>
        /// <param name="buildingName">Building name</param>
        /// <param name="list">List to add to/remove from</param>
        /// <param name="inList">True if the building should be in the list, false if it should be removed</param>
        private void ColorizeListEntry(string buildingName, List<string> list, bool inList)
        {
            // First, see if the given list contains the name of the current building.
            if (list.Contains(buildingName))
            {
                // In list - if the checkbox is not checked, remove the entry.
                if (!inList)
                {
                    list.RemoveAll((string building) => building == buildingName);
                }
            }
            else
            {
                // Not in list - if the checkbox is checked, add the entry.
                if (inList)
                {
                    list.Add(buildingName);
                }
            }
        }


        /// <summary>
        /// Updates RGB textfields with current color values.
        /// </summary>
        private void UpdateTextFields()
        {
            // Update textfield values using localized formats, after null checks to catch when called prior to picker setup.
            if (redField != null)
            {
                redField.text = currentRed.ToString("N3", LocaleManager.cultureInfo);
            }
            if (greenField != null)
            {
                greenField.text = currentGreen.ToString("N3", LocaleManager.cultureInfo);
            }
            if (blueField != null)
            {
                blueField.text = currentBlue.ToString("N3", LocaleManager.cultureInfo);
            }
        }


        /// <summary>
        /// Handles textfield keydown events, checking for completion of text entry via return, enter, or tab keys.
        /// Calls the given parser to process text and moves focus to the given textfield.
        /// </summary>
        /// <param name="keyEvent">KeyEvent to process</param>
        /// <param name="color">Color reference field</param>
        /// <param name="textField">Current textfield</param>
        /// <param name="nextField">Textfield to move to on sucessful parse</param>
        private void TextEnter(UIKeyEventParameter keyEvent, ref float color, UITextField textField, UITextField nextField)
        {
            KeyCode keyCode = keyEvent.keycode;

            if (keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter || keyCode == KeyCode.Tab)
            {
                // Use it up before the parent handler closes the ColorPicker!
                keyEvent.Use();

                if (ParseRGB(textField, ref color))
                {
                    // Parsing was successful - move focus to next textfield.
                    nextField.Focus();
                }
            }
        }


        /// <summary>
        /// Parses the red textfield and updates the building color.
        /// </summary>
        /// <returns>True if successful parse, false otherwise</returns>
        private bool ParseRGB(UITextField textField, ref float colorRef)
        {
            // Try to parse textfield.
            if (float.TryParse(textField.text, out float color))
            {
                // Successful parse - update and apply the new colour.
                colorRef = Mathf.Clamp(color, 0, 1);

                // Suspend events before assigning the new color, otherwise we get circular overwrites and cumulative errors.
                suspendEvents = true;
                picker.color = new Color { r = currentRed, g = currentGreen, b = currentBlue };
                suspendEvents = false;

                // Update text fields with parsed values to provide user feedback.
                UpdateTextFields();
                return true;
            }

            // If we got here, we didn't successfully parse.
            return false;
        }


        /// <summary>
        /// Creates a pushbutton.
        /// </summary>
        /// <param name="parent">Parent component</param>
        /// <param name="text">Button text label</param>
        /// <param name="xPos">Button relative X position</param>
        /// <returns>New pushbutton</returns>
        private UIButton CreateButton(UIComponent parent, string text, float xPos) => UIControls.AddButton(parent, xPos, ButtonY, text, ButtonWidth, 20f, 0.8f);


        /// <summary>
        /// Creates a checkbox.
        /// </summary>
        /// <param name="parent">Parent component</param>
        /// <param name="text">Button text label</param>
        /// <param name="xPos">Button relative X position</param>
        /// <returns>New checkbox</returns>
        private UICheckBox CreateCheckBox(UIComponent parent, string text, float xPos) => UIControls.AddCheckBox(parent, xPos, CheckBoxY, text, tooltip: Translations.Translate("PAINTER-RELOAD-REQUIRED"));


        /// <summary>
        /// Creates a textfield with label to the left.
        /// </summary>
        /// <param name="parent">Parent component</param>
        /// <param name="textKey">Textfield text label translation key</param>
        /// <param name="xPos">Text relative X position</param>
        /// <param name="toolTipKey">Tooltip translation key</param>
        /// <returns>New checkbox</returns>
        private UITextField CreateTextField(UIComponent parent, string textKey, float xPos, string toolTipKey) => UIControls.LabelledTextField(parent, xPos, TextFieldY, Translations.Translate(textKey), 50f, 16f, 0.8f, 3, Translations.Translate(toolTipKey));
    }
}
