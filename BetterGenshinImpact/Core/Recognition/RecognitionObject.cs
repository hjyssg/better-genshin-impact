﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition;

/// <summary>
/// 识别对象
/// </summary>
[Serializable]
public class RecognitionObject
{
    public RecognitionTypes RecognitionType { get; set; }

    /// <summary>
    /// 感兴趣的区域
    /// </summary>
    public Rect RegionOfInterest { get; set; }

    public string? Name { get; set; }

    #region 模板匹配

    /// <summary>
    /// 模板匹配的对象(彩色)
    /// </summary>
    public Mat? TemplateImageMat { get; set; }

    /// <summary>
    /// 模板匹配的对象(灰色)
    /// </summary>
    public Mat? TemplateImageGreyMat { get; set; }

    /// <summary>
    /// 模板匹配阈值。可选，默认 0.8 。
    /// </summary>
    public double Threshold { get; set; } = 0.8;

    /// <summary>
    /// 是否使用 3 通道匹配。可选，默认 false 。
    /// </summary>
    public bool Use3Channels { get; set; } = false;

    /// <summary>
    /// 模板匹配算法。可选，默认 CCoeffNormed 。
    /// https://docs.opencv.org/4.x/df/dfb/group__imgproc__object.html
    /// </summary>
    public TemplateMatchModes TemplateMatchMode { get; set; } = TemplateMatchModes.CCoeffNormed;

    /// <summary>
    /// 匹配模板遮罩，指定图片中的某种色彩不需要匹配
    /// 使用时，需要将模板图片的背景色设置为纯黑色，即 #000000
    /// </summary>
    public bool UseMask { get; set; } = false;

    /// <summary>
    /// 不需要匹配的颜色，默认绿色
    /// UseMask = true 的时候有用
    /// </summary>
    public Color MaskColor { get; set; } = Color.FromArgb(0, 255, 0);

    public Mat? MaskMat { get; set; }

    /// <summary>
    /// 匹配成功时，是否在屏幕上绘制矩形框。可选，默认 false 。
    /// true 时 Name 必须有值。
    /// </summary>
    public bool DrawOnWindow { get; set; } = false;

    /// <summary>
    /// DrawOnWindow 为 true 时，绘制的矩形框的颜色。可选，默认红色。
    /// </summary>
    public Pen DrawOnWindowPen = new(Color.Red, 2);

    public RecognitionObject InitTemplate()
    {
        if (TemplateImageMat != null && TemplateImageGreyMat == null)
        {
            TemplateImageGreyMat = new Mat();
            Cv2.CvtColor(TemplateImageMat, TemplateImageGreyMat, ColorConversionCodes.BGR2GRAY);
        }

        if (UseMask && TemplateImageMat != null && MaskMat == null)
        {
            MaskMat = OpenCvCommonHelper.Threshold(TemplateImageMat, MaskColor.ToScalar());
        }
        return this;
    }

    #endregion

    #region 颜色匹配

    /// <summary>
    /// 颜色匹配方式。即 cv::ColorConversionCodes。可选，默认 4 (RGB)。
    /// 常用值：4 (RGB, 3 通道), 40 (HSV, 3 通道), 6 (GRAY, 1 通道)。
    /// https://docs.opencv.org/4.x/d8/d01/group__imgproc__color__conversions.html
    /// </summary>
    public ColorConversionCodes ColorConversionCode { get; set; } = ColorConversionCodes.BGR2RGB;

    public Color LowerColor { get; set; }
    public Color UpperColor { get; set; }

    /// <summary>
    /// 符合的点的数量要求。可选，默认 1
    /// </summary>
    public int MatchCount { get; set; } = 1;

    #endregion

    #region OCR文字识别

    /// <summary>
    /// OCR 引擎。可选，默认 Media.Ocr。
    /// </summary>
    public OcrEngineTypes OcrEngine { get; set; } = OcrEngineTypes.Paddle;

    /// <summary>
    /// 部分文字识别结果不准确，进行替换。可选。
    /// </summary>
    public Dictionary<string, string[]> ReplaceDictionary { get; set; } = new();

    /// <summary>
    /// 包含匹配
    /// 多个值全匹配的情况下才算成功
    /// 复杂情况请用下面的正则匹配
    /// </summary>
    public List<string> AllContainMatchText { get; set; } = new();

    /// <summary>
    /// 包含匹配
    /// 一个值匹配就算成功
    /// </summary>
    public List<string> OneContainMatchText { get; set; } = new();

    /// <summary>
    /// 正则匹配
    /// 多个值全匹配的情况下才算成功
    /// </summary>
    public List<string> RegexMatchText { get; set; } = new();

    #endregion
}