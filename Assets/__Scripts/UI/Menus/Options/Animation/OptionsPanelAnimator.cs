using System;
using DG.Tweening;
using SaturnGame.Settings;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace SaturnGame.UI
{
public class OptionsPanelAnimator : MonoBehaviour
{
    [SerializeField] private GameObject linearPanelGroup;
    [SerializeField] private GameObject radialPanelGroup;

    [SerializeField] private List<OptionPanelLinear> linearPanels;
    [SerializeField] private List<OptionPanelRadial> radialPanels;

    [SerializeField] private OptionPanelPrimary primaryPanel;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private CanvasGroup radialCenterGroup;
    [SerializeField] private RectTransform panelGroupRect;
    [SerializeField] private RectTransform gradientRect;
    [SerializeField] private RectTransform spinnyThingRect;
    [SerializeField] private RectTransform navigatorRect;
    [SerializeField] private RectTransform headerRect;
    [SerializeField] private RectMask2D panelMask;
    [SerializeField] private Image glassImage;
    [SerializeField] private GameObject radialCoverRing;
    [SerializeField] private GameObject radialCoverBackground;

    [SerializeField] private GameObject subTypeChartPreview;
    [SerializeField] private GameObject subTypeSprite;
    [SerializeField] private GameObject subTypeText;
    [SerializeField] private GameObject subTypeOffset;

    [SerializeField] private TextMeshProUGUI radialMenuTitle;
    [SerializeField] private TextMeshProUGUI radialMenuDescription;

    [SerializeField] private TextMeshProUGUI radialOptionTitle;
    [SerializeField] private TextMeshProUGUI radialOptionDescription;
    
    [SerializeField] private Image radialOptionImage;
    
    [SerializeField] private RectTransform radialOffsetNote;
    [SerializeField] private TextMeshProUGUI radialOffsetValue;

private Sequence currentSequence;

    private int LinearCenterIndex { get; set; }
    private int LinearWrapIndex { get; set; }
    private int RadialCenterIndex { get; set; }
    private int RadialWrapIndex { get; set; }
    private int LinearHalfCount => (int)(linearPanels.Count * 0.5f);
    private int RadialHalfCount => (int)(radialPanels.Count * 0.5f);

    public enum MoveDirection
    {
        Up = -1,
        Down = 1,
    }

    public void Anim_ShiftPanels(MoveDirection direction, int currentIndex, [NotNull] UIScreen screen)
    {
        const float time = 0.05f;
        const Ease ease = Ease.Linear;

        if (screen.ScreenType is UIScreen.UIScreenType.Radial) animateRadial();
        else animateLinear();
        return;

        void animateLinear()
        {
            LinearCenterIndex = SaturnMath.Modulo(LinearCenterIndex + (int)direction, linearPanels.Count);
            LinearWrapIndex = SaturnMath.Modulo(LinearCenterIndex + LinearHalfCount * (int)direction,
                linearPanels.Count);

            for (int i = 0; i < linearPanels.Count; i++)
            {
                OptionPanelLinear panel = linearPanels[i];
                int index = SaturnMath.Modulo(LinearHalfCount - LinearCenterIndex + i, linearPanels.Count);
                bool wrap = i == LinearWrapIndex;

                Vector2 position = GetLinearPosition(index);
                float scale = GetLinearScale(index);
                float duration = wrap ? 0 : time;

                panel.Rect.DOAnchorPos(position, duration).SetEase(ease);
                panel.Rect.DOScale(scale, duration).SetEase(ease);

                if (!wrap) continue;

                int itemIndex = currentIndex + LinearHalfCount * (int)direction;
                bool active = itemIndex >= 0 && itemIndex < screen.VisibleListItems.Count;
                panel.gameObject.SetActive(active);
                if (active) SetPanelLinear(screen, screen.VisibleListItems[itemIndex], panel);
            }
        }

        void animateRadial()
        {
            RadialCenterIndex = SaturnMath.Modulo(RadialCenterIndex + (int)direction, radialPanels.Count);
            RadialWrapIndex = SaturnMath.Modulo(RadialCenterIndex + RadialHalfCount * (int)direction,
                radialPanels.Count);

            for (int i = 0; i < radialPanels.Count; i++)
            {
                OptionPanelRadial panel = radialPanels[i];
                int index = SaturnMath.Modulo(RadialHalfCount - RadialCenterIndex + i, radialPanels.Count);
                bool wrap = i == RadialWrapIndex;

                Vector3 angle = GetRadialAngle(index);
                float duration = wrap ? 0 : time;

                panel.Rect.DORotate(angle, duration).SetEase(ease);

                if (!wrap) continue;

                int itemIndex = currentIndex + RadialHalfCount * (int)direction;
                bool active = itemIndex >= 0 && itemIndex < screen.VisibleListItems.Count;
                panel.gameObject.SetActive(active);
                if (active) SetPanelRadial(screen.VisibleListItems[itemIndex], panel);
            }
        }
    }

    public void Anim_ShowPanels([NotNull] UIScreen previous, [NotNull] UIScreen next)
    {
        bool prevLinear = previous.ScreenType is UIScreen.UIScreenType.LinearSimple or UIScreen.UIScreenType.LinearDetailed;
        bool nextLinear = next.ScreenType is UIScreen.UIScreenType.LinearSimple or UIScreen.UIScreenType.LinearDetailed;

        if (!nextLinear)
        {
            Anim_ShowPanelsRadial();
            return;
        }

        if (prevLinear) Anim_ShowPanelsLinearPartial();
        else Anim_ShowPanelsLinearFull();

        // Linear -> Linear => Partial
        // Linear -> Radial => Radial
        // Radial -> Linear => Full
        // Radial -> Radial => Radial
    }

    public void Anim_HidePanels([NotNull] UIScreen previous, [NotNull] UIScreen next)
    {
        bool prevLinear = previous.ScreenType is UIScreen.UIScreenType.LinearSimple or UIScreen.UIScreenType.LinearDetailed;
        bool nextLinear = next.ScreenType is UIScreen.UIScreenType.LinearSimple or UIScreen.UIScreenType.LinearDetailed;

        if (!prevLinear)
        {
            Anim_HidePanelsRadial();
            return;
        }

        if (nextLinear) Anim_HidePanelsLinearPartial();
        else Anim_HidePanelsLinearFull();

        // Linear -> Linear => Partial
        // Linear -> Radial => Full
        // Radial -> Linear => Radial
        // Radial -> Radial => Radial
    }


    private void Anim_ShowPanelsLinearPartial()
    {
        // 1 frame = 32ms
        // Move panels 6 frames OutQuad
        // Fade Panels 6 frames OutQuad

        const float frame = 0.032f;
        currentSequence.Kill(true);

        panelMask.enabled = true;
        radialCenterGroup.gameObject.SetActive(false);
        radialCoverRing.SetActive(false);
        radialCoverBackground.SetActive(false);

        panelGroup.alpha = 0;
        panelGroupRect.anchoredPosition = new(-250, 0);
        panelGroupRect.eulerAngles = new(0, 0, 0);

        currentSequence = DOTween.Sequence();
        currentSequence.Join(panelGroup.DOFade(1, frame * 6).SetEase(Ease.OutQuad));
        currentSequence.Join(panelGroupRect.DOAnchorPosX(0, frame * 6).SetEase(Ease.OutQuad));
    }

    private void Anim_HidePanelsLinearPartial()
    {
        // 1 frame = 32ms
        // Move panels 4 frames InQuad

        // wait 2 frames
        // Fade panels out 2 frames Linear

        const float frame = 0.032f;
        currentSequence.Kill(true);

        panelMask.enabled = true;
        radialCenterGroup.gameObject.SetActive(false);
        radialCoverRing.SetActive(false);
        radialCoverBackground.SetActive(false);

        panelGroupRect.anchoredPosition = new(0, 0);
        panelGroupRect.eulerAngles = new(0, 0, 0);
        panelGroup.alpha = 1;

        currentSequence = DOTween.Sequence();
        currentSequence.Join(panelGroupRect.DOAnchorPosX(-250, frame * 4).SetEase(Ease.InQuad));
        currentSequence.Insert(frame * 2, panelGroup.DOFade(0, frame * 2).SetEase(Ease.Linear));
    }


    private void Anim_ShowPanelsLinearFull()
    {
        // 1 frame = 32ms
        // Move panels 6 frames OutQuad
        // Fade Panels 6 frames OutQuad
        // Move Navigator 6 frames OutQuad

        // wait 2 frames
        // Move Gradient 4 frames OutQuad

        const float frame = 0.032f;
        currentSequence.Kill(true);

        panelMask.enabled = true;
        radialCenterGroup.gameObject.SetActive(false);
        radialCoverRing.SetActive(false);
        radialCoverBackground.SetActive(false);

        panelGroup.alpha = 0;
        panelGroupRect.eulerAngles = new(0, 0, 0);
        panelGroupRect.anchoredPosition = new(-250, 0);
        navigatorRect.anchoredPosition = new(1250, -400);
        
        gradientRect.anchoredPosition = new(0, -235);
        headerRect.anchoredPosition = new(0, 250);

        currentSequence = DOTween.Sequence();
        currentSequence.Join(panelGroup.DOFade(1, frame * 3).SetEase(Ease.Linear));
        currentSequence.Join(panelGroupRect.DOAnchorPosX(0, frame * 6).SetEase(Ease.OutQuad));
        currentSequence.Join(navigatorRect.DOAnchorPosX(270, frame * 6).SetEase(Ease.OutQuad));
        
        currentSequence.Insert(frame * 2, gradientRect.DOAnchorPosY(0, frame * 4));
        currentSequence.Insert(frame * 2, headerRect.DOAnchorPosX(-420, frame * 4));
    }

    public void Anim_HidePanelsLinearFull()
    {
        // 1 frame = 32ms
        // Move panels 4 frames InQuad
        // Move Gradient 4 frames InQuad

        // wait 2 frames
        // Fade panels out 2 frames Linear
        // Move Navigator 6 frames
        // Fade Navigator 6 frames Linear

        const float frame = 0.032f;
        currentSequence.Kill(true);

        panelMask.enabled = true;
        radialCenterGroup.gameObject.SetActive(false);
        radialCoverRing.SetActive(false);
        radialCoverBackground.SetActive(false);

        panelGroupRect.anchoredPosition = new(0, 0);
        panelGroupRect.eulerAngles = new(0, 0, 0);
        gradientRect.anchoredPosition = new(0, 0);
        headerRect.anchoredPosition = new(-420, 250);

        panelGroup.alpha = 1;
        navigatorRect.anchoredPosition = new(270, -400);

        currentSequence = DOTween.Sequence();
        currentSequence.Join(panelGroupRect.DOAnchorPosX(-250, frame * 4).SetEase(Ease.InQuad));
        currentSequence.Join(gradientRect.DOAnchorPosY(-235, frame * 4).SetEase(Ease.InQuad));
        currentSequence.Join(headerRect.DOAnchorPosX(0, frame * 4).SetEase(Ease.InQuad));

        currentSequence.Insert(frame * 2, panelGroup.DOFade(0, frame * 2).SetEase(Ease.Linear));
        currentSequence.Insert(frame * 2, navigatorRect.DOAnchorPosX(1250, frame * 6).SetEase(Ease.InQuad));
    }


    private void Anim_ShowPanelsRadial()
    {
        // Fade radial center 4 frames InQuad

        // wait 4 frames
        // Spin panels 6 frames OutQuad
        // Fade panels 6 frames OutQuad

        const float frame = 0.032f;
        currentSequence.Kill(true);

        panelMask.enabled = false;
        radialCenterGroup.gameObject.SetActive(true);
        radialCoverRing.SetActive(true);
        radialCoverBackground.SetActive(true);

        panelGroupRect.anchoredPosition = new(0, 0);
        panelGroupRect.eulerAngles = new(0, 0, 120);
        panelGroup.alpha = 0;
        radialCenterGroup.DOFade(0, 0);

        currentSequence = DOTween.Sequence();
        currentSequence.Join(radialCenterGroup.DOFade(1, frame * 4).SetEase(Ease.InQuad));

        currentSequence.Insert(frame * 4,
            panelGroupRect.DORotate(new(0, 0, 0), frame * 6).SetEase(Ease.OutQuad));
        currentSequence.Insert(frame * 4, panelGroup.DOFade(1, frame * 6).SetEase(Ease.OutQuad));
    }

    private void Anim_HidePanelsRadial()
    {
        // Spin panels 3 frames Linear
        // Fade panels 3 frames OutQuad

        // wait 6 frames
        // Scale preview 4 frames InBounce

        const float frame = 0.032f;
        currentSequence.Kill(true);

        panelMask.enabled = false;
        radialCenterGroup.gameObject.SetActive(true);
        radialCoverRing.SetActive(true);
        radialCoverBackground.SetActive(true);

        panelGroupRect.eulerAngles = new(0, 0, 0);
        panelGroup.alpha = 1;

        currentSequence = DOTween.Sequence();
        currentSequence.Join(panelGroup.DOFade(0, frame * 3).SetEase(Ease.OutQuad));
    }

    public void Anim_UpdateRadialOffsetOption(int index)
    {
        const float increments = 0.89f;
        
        int offsetIndex = index - 100;
        radialOffsetNote.anchoredPosition = new(0, increments * offsetIndex);
        radialOffsetValue.text = (offsetIndex >= 0 ? "+" : "") + (offsetIndex * 0.1f).ToString("N1");;
    }

    public void SetPrimaryPanel([NotNull] UIListItem item)
    {
        bool dynamic = item.ItemType is UIListItem.ItemTypes.SubMenu &&
                       item.SubtitleType is UIListItem.SubtitleTypes.Dynamic;

        primaryPanel.Title = item.Title;
        primaryPanel.Subtitle = dynamic ? GetSelectedString(item) : item.Subtitle;

        radialOptionTitle.text = item.Title;
        radialOptionDescription.text = dynamic ? GetSelectedString(item) : item.Subtitle;
        
        primaryPanel.SetRadialPanelColor(item);

        radialOptionImage.sprite = item.Sprite;
    }

    public void GetPanels([NotNull] UIScreen screen, int currentIndex = 0)
    {
        currentIndex = Mathf.Clamp(currentIndex, 0, screen.VisibleListItems.Count);

        if (screen.VisibleListItems.Count == 0) return;
        
        if (screen.ScreenType is UIScreen.UIScreenType.Radial) getRadial();
        else getLinear();
        return;

        void getLinear()
        {
            linearPanelGroup.SetActive(true);
            radialPanelGroup.SetActive(false);

            SetPrimaryPanel(screen.VisibleListItems[currentIndex]);
            primaryPanel.SetType(screen.ScreenType);

            LinearCenterIndex = LinearHalfCount;

            for (int i = 0; i < linearPanels.Count; i++)
            {
                OptionPanelLinear panel = linearPanels[i];
                int itemIndex = currentIndex - LinearCenterIndex + i;

                if (itemIndex >= screen.VisibleListItems.Count || itemIndex < 0)
                {
                    panel.gameObject.SetActive(false);
                    continue;
                }

                UIListItem item = screen.VisibleListItems[itemIndex];
                SetPanelLinear(screen, item, panel);

                Vector2 position = GetLinearPosition(i);
                float scale = GetLinearScale(i);

                panel.Rect.anchoredPosition = position;
                panel.Rect.localScale = Vector3.one * scale;
                panel.gameObject.SetActive(true);
            }
        }

        void getRadial()
        {
            linearPanelGroup.SetActive(false);
            radialPanelGroup.SetActive(true);

            SetPrimaryPanel(screen.VisibleListItems[currentIndex]);
            primaryPanel.SetType(screen.ScreenType);

            RadialCenterIndex = RadialHalfCount;

            for (int i = 0; i < radialPanels.Count; i++)
            {
                OptionPanelRadial panel = radialPanels[i];
                int itemIndex = currentIndex - RadialCenterIndex + i;

                if (itemIndex >= screen.VisibleListItems.Count || itemIndex < 0)
                {
                    panel.gameObject.SetActive(false);
                    continue;
                }

                UIListItem item = screen.VisibleListItems[itemIndex];
                SetPanelRadial(item, panel);

                Vector3 angle = GetRadialAngle(i);

                panel.Rect.eulerAngles = angle;
                panel.gameObject.SetActive(true);
            }

            radialMenuTitle.text = screen.ScreenTitle;
            radialMenuDescription.text = screen.ScreenSubtitle;
            
            subTypeChartPreview.SetActive(false);
            subTypeText.SetActive(false);
            subTypeSprite.SetActive(false);
            subTypeOffset.SetActive(false);

            switch (screen.RadialSubType)
            {
                case UIScreen.RadialScreenSubType.ChartPreview: 
                {
                    subTypeChartPreview.SetActive(true);
                    break;
                }
                
                case UIScreen.RadialScreenSubType.Sprites: 
                {
                    subTypeSprite.SetActive(true);
                    break;
                }
                
                case UIScreen.RadialScreenSubType.Text: 
                {
                    subTypeText.SetActive(true);
                    break;
                }

                case UIScreen.RadialScreenSubType.Offset:
                {
                    subTypeOffset.SetActive(true);
                    Anim_UpdateRadialOffsetOption(currentIndex);
                    break;
                }
            }
        }
    }

    private static string GetSelectedString([NotNull] UIListItem item)
    {
        Type parameterType;
        object parameterValue;
        try
        {
            (parameterType, parameterValue) =
                SettingsManager.Instance.PlayerSettings.GetParameter(item.SettingsBinding);
        }
        catch (ArgumentException e)
        {
            Debug.LogWarning(e);
            return "???";
        }

        if (item.NextScreen == null)
        {
            Debug.LogWarning($"NextScreen of [{item.Title}] has not been set!");
            return "???";
        }

        if (item.NextScreen.VisibleListItems.Count == 0)
        {
            Debug.LogWarning($"NextScreen of [{item.Title}] has no List Items!");
            return "???";
        }

        UIListItem selectedItem = item.NextScreen.VisibleListItems.FirstOrDefault(x =>
            x.MatchesParameterValue(parameterType, parameterValue));

        if (selectedItem != null) return selectedItem.Title;

        Debug.LogWarning($"No item with matching value {parameterValue} was found!");
        return "???";
    }

    private static Vector2 GetLinearPosition(int index)
    {
        float[] posX = { 120, 55, 20, 20, 20, 55, 120 };
        float[] posY = { 350, 250, 150, 0, -150, -250, -350 };
        return new(posX[index], posY[index]);
    }

    private static float GetLinearScale(int index)
    {
        float[] scales = { 0.85f, 0.85f, 1, 1, 1, 0.85f, 0.85f };
        return scales[index];
    }

    private static Vector3 GetRadialAngle(int index)
    {
        // This is a little jank but... it works.
        float[] angles =
        {
            -99, -99, -99, -99, -99, -99, -99, -99, -81, -63, -45, -27, 0, 27, 45, 63, 81, 99, 117, 135, 153, 171,
            189, 207, 225,
        };
        return new(0, 0, angles[index]);
    }

    private static void SetPanelLinear([NotNull] UIScreen screen, [NotNull] UIListItem item,
        [NotNull] OptionPanelLinear panel)
    {
        bool dynamic = item.ItemType is UIListItem.ItemTypes.SubMenu &&
                       item.SubtitleType is UIListItem.SubtitleTypes.Dynamic;

        panel.Title = item.Title;
        panel.Subtitle = dynamic ? GetSelectedString(item) : item.Subtitle;
        panel.SetType(screen.ScreenType);
    }

    private static void SetPanelRadial([NotNull] UIListItem item, [NotNull] OptionPanelRadial panel)
    {
        panel.Title = item.Title;
        panel.SetRadialPanelColor(item);
    }
}
}
