﻿using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.View.Drawable;
using GeniusInvokationAutoToy.Utils;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model;

/// <summary>
/// 对局
/// </summary>
public class Duel
{
    private readonly ILogger<Duel> _logger = App.GetLogger<Duel>();

    public Character CurrentCharacter { get; set; }
    public Character[] Characters { get; set; } = new Character[4];

    /// <summary>
    /// 行动指令队列
    /// </summary>
    public List<ActionCommand> ActionCommandQueue { get; set; } = new List<ActionCommand>();

    /// <summary>
    /// 当前回合数
    /// </summary>
    public int RoundNum { get; set; } = 1;

    /// <summary>
    /// 角色牌位置
    /// </summary>
    public List<Rect> CharacterCardRects { get; set; }

    /// <summary>
    /// 手牌数量
    /// </summary>
    public int CurrentCardCount { get; set; } = 0;

    /// <summary>
    /// 骰子数量
    /// </summary>
    public int CurrentDiceCount { get; set; } = 0;


    public CancellationTokenSource Cts { get; set; }


    public async Task RunAsync(GeniusInvokationTaskParam taskParam)
    {
        await Task.Run(() => { Run(taskParam); });
    }

    public void Run(GeniusInvokationTaskParam taskParam)
    {
        Cts = taskParam.Cts;
        try
        {
            LogScreenResolution();
            _logger.LogInformation("========================================");
            _logger.LogInformation("→ {Text}", "全自动七圣召唤，启动！");

            GeniusInvokationControl.GetInstance().Init(taskParam);
            SystemControl.ActivateWindow();

            // 对局准备 选择初始手牌
            GeniusInvokationControl.GetInstance().CommonDuelPrepare();


            // 获取角色区域
            try
            {
                CharacterCardRects = Retry.Do(() => GeniusInvokationControl.GetInstance().GetCharacterRects(), TimeSpan.FromSeconds(1.5), 3);
            }
            catch
            {
                // ignored
            }

            if (CharacterCardRects is not { Count: 3 })
            {
                CharacterCardRects = new List<Rect>();
                var defaultCharacterCardRects = TaskContext.Instance().Config.AutoGeniusInvokationConfig.DefaultCharacterCardRects;
                var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
                for (var i = 0; i < defaultCharacterCardRects.Count; i++)
                {
                    CharacterCardRects.Add(defaultCharacterCardRects[i].Multiply(assetScale));
                }

                _logger.LogInformation("获取角色区域失败，使用默认区域");
            }

            for (var i = 1; i < 4; i++)
            {
                Characters[i].Area = CharacterCardRects[i - 1];
            }

            // 出战角色
            CurrentCharacter = ActionCommandQueue[0].Character;
            CurrentCharacter.ChooseFirst();

            // 开始执行回合
            while (true)
            {
                _logger.LogInformation("--------------第{RoundNum}回合--------------", RoundNum);
                ClearCharacterStatus(); // 清空单回合的异常状态
                if (RoundNum == 1)
                {
                    CurrentCardCount = 5;
                }
                else
                {
                    CurrentCardCount += 2;
                }

                CurrentDiceCount = 8;

                // 预计算本回合内的所有可能的元素
                var elementSet = PredictionDiceType();

                // 0 投骰子
                GeniusInvokationControl.GetInstance().ReRollDice(elementSet.ToArray());

                // 等待到我的回合 // 投骰子动画时间是不确定的  // 可能是对方先手
                GeniusInvokationControl.GetInstance().WaitForMyTurn(this, 1000);

                // 开始执行行动
                while (true)
                {
                    // 没骰子了就结束行动
                    _logger.LogInformation("行动开始,当前骰子数[{CurrentDiceCount}],当前手牌数[{CurrentCardCount}]", CurrentDiceCount, CurrentCardCount);
                    if (CurrentDiceCount <= 0)
                    {
                        _logger.LogInformation("骰子已经用完");
                        GeniusInvokationControl.GetInstance().Sleep(2000);
                        break;
                    }

                    // 每次行动前都要检查当前角色
                    CurrentCharacter = GeniusInvokationControl.GetInstance().WhichCharacterActiveWithRetry(this);

                    // 行动前重新确认骰子数量
                    var diceCountFromOcr = GeniusInvokationControl.GetInstance().GetDiceCountByOcr();
                    if (diceCountFromOcr != -10)
                    {
                        var diceDiff = Math.Abs(CurrentDiceCount - diceCountFromOcr);
                        if (diceDiff is > 0 and <= 2)
                        {
                            _logger.LogInformation("可能存在场地牌影响了骰子数[{CurrentDiceCount}] -> [{DiceCountFromOcr}]", CurrentDiceCount, diceCountFromOcr);
                            CurrentDiceCount = diceCountFromOcr;
                        }
                        else if (diceDiff > 2)
                        {
                            _logger.LogWarning(" OCR识别到的骰子数[{DiceCountFromOcr}]和计算得出的骰子数[{CurrentDiceCount}]差距较大，舍弃结果", diceCountFromOcr, CurrentDiceCount);
                        }
                    }

                    var alreadyExecutedActionIndex = new List<int>();
                    var alreadyExecutedActionCommand = new List<ActionCommand>();
                    var i = 0;
                    for (i = 0; i < ActionCommandQueue.Count; i++)
                    {
                        var actionCommand = ActionCommandQueue[i];
                        // 指令中的角色未被打败、角色有异常状态 跳过指令
                        if (actionCommand.Character.IsDefeated || actionCommand.Character.StatusList?.Count > 0)
                        {
                            continue;
                        }

                        // 当前出战角色身上存在异常状态的情况下不执行本角色的指令
                        if (CurrentCharacter.StatusList?.Count > 0 &&
                            actionCommand.Character.Index == CurrentCharacter.Index)
                        {
                            continue;
                        }


                        // 1. 判断切人
                        if (CurrentCharacter.Index != actionCommand.Character.Index)
                        {
                            if (CurrentDiceCount >= 1)
                            {
                                actionCommand.SwitchLater();
                                CurrentDiceCount--;
                                alreadyExecutedActionIndex.Add(-actionCommand.Character.Index); // 标记为已执行
                                var switchAction = new ActionCommand
                                {
                                    Character = CurrentCharacter,
                                    Action = ActionEnum.SwitchLater,
                                    TargetIndex = actionCommand.Character.Index
                                };
                                alreadyExecutedActionCommand.Add(switchAction);
                                _logger.LogInformation("→指令执行完成：{Action}", switchAction);
                                break;
                            }
                            else
                            {
                                _logger.LogInformation("骰子不足以进行下一步：切换角色 {CharacterIndex}", actionCommand.Character.Index);
                                break;
                            }
                        }

                        // 2. 判断使用技能
                        if (actionCommand.GetAllDiceUseCount() > CurrentDiceCount)
                        {
                            _logger.LogInformation("骰子不足以进行下一步：{Action}", actionCommand);
                            break;
                        }
                        else
                        {
                            bool useSkillRes = actionCommand.UseSkill(this);
                            if (useSkillRes)
                            {
                                CurrentDiceCount -= actionCommand.GetAllDiceUseCount();
                                alreadyExecutedActionIndex.Add(i);
                                alreadyExecutedActionCommand.Add(actionCommand);
                                _logger.LogInformation("→指令执行完成：{Action}", actionCommand);
                            }
                            else
                            {
                                _logger.LogWarning("→指令执行失败(可能是手牌不够)：{Action}", actionCommand);
                                GeniusInvokationControl.GetInstance().Sleep(1000);
                                GeniusInvokationControl.GetInstance().ClickGameWindowCenter();
                            }

                            break;
                        }
                    }


                    if (alreadyExecutedActionIndex.Count != 0)
                    {
                        foreach (var index in alreadyExecutedActionIndex)
                        {
                            if (index >= 0)
                            {
                                ActionCommandQueue.RemoveAt(index);
                            }
                        }

                        alreadyExecutedActionIndex.Clear();
                        // 等待对方行动完成 （开大的时候等待时间久一点）
                        var sleepTime = ComputeWaitForMyTurnTime(alreadyExecutedActionCommand);
                        GeniusInvokationControl.GetInstance().WaitForMyTurn(this, sleepTime);
                        alreadyExecutedActionCommand.Clear();
                    }
                    else
                    {
                        // 如果没有任何指令可以执行 则跳出循环
                        // TODO 也有可能是角色死亡/所有角色被冻结导致没有指令可以执行
                        //if (i >= ActionCommandQueue.Count)
                        //{
                        //    throw new DuelEndException("策略中所有指令已经执行完毕，结束自动打牌");
                        //}
                        GeniusInvokationControl.GetInstance().Sleep(2000);
                        break;
                    }

                    if (ActionCommandQueue.Count == 0)
                    {
                        throw new DuelEndException("策略中所有指令已经执行完毕，结束自动打牌");
                    }
                }

                // 回合结束
                GeniusInvokationControl.GetInstance().Sleep(1000);
                _logger.LogInformation("我方点击回合结束");
                GeniusInvokationControl.GetInstance().RoundEnd();

                // 等待对方行动+回合结算
                GeniusInvokationControl.GetInstance().WaitOpponentAction(this);
                RoundNum++;
            }
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogInformation(ex.Message);
        }
        catch (DuelEndException ex)
        {
            _logger.LogInformation(ex.Message);
            _logger.LogInformation("对局结束");
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex.Message);
            Debug.WriteLine(ex.StackTrace);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskSettingsPageViewModel.SetSwitchAutoGeniusInvokationButtonText(false);
            _logger.LogInformation("← {Text}", "退出全自动七圣召唤");
            taskParam.Dispatcher.StartTimer();
        }
    }

    private HashSet<ElementalType> PredictionDiceType()
    {
        var actionUseDiceSum = 0;
        var elementSet = new HashSet<ElementalType>
        {
            ElementalType.Omni
        };
        for (var i = 0; i < ActionCommandQueue.Count; i++)
        {
            var actionCommand = ActionCommandQueue[i];

            // 角色未被打败的情况下才能执行
            if (actionCommand.Character.IsDefeated)
            {
                continue;
            }

            // 通过骰子数量判断是否可以执行

            // 1. 判断切人
            if (i > 0 && actionCommand.Character.Index != ActionCommandQueue[i - 1].Character.Index)
            {
                actionUseDiceSum++;
                if (actionUseDiceSum > CurrentDiceCount)
                {
                    break;
                }
                else
                {
                    elementSet.Add(actionCommand.GetDiceUseElementType());
                    //executeActionIndex.Add(-actionCommand.Character.Index);
                }
            }

            // 2. 判断使用技能
            actionUseDiceSum += actionCommand.GetAllDiceUseCount();
            if (actionUseDiceSum > CurrentDiceCount)
            {
                break;
            }
            else
            {
                elementSet.Add(actionCommand.GetDiceUseElementType());
                //executeActionIndex.Add(i);
            }
        }

        return elementSet;
    }

    public void ClearCharacterStatus()
    {
        foreach (var character in Characters)
        {
            character?.StatusList?.Clear();
        }
    }

    /// <summary>
    /// 根据前面执行的命令计算等待时间
    /// 大招等待15秒
    /// 快速切换等待3秒
    /// </summary>
    /// <param name="alreadyExecutedActionCommand"></param>
    /// <returns></returns>
    private int ComputeWaitForMyTurnTime(List<ActionCommand> alreadyExecutedActionCommand)
    {
        foreach (var command in alreadyExecutedActionCommand)
        {
            if (command.Action == ActionEnum.UseSkill && command.TargetIndex == 1)
            {
                return 15000;
            }

            // 莫娜切换等待3秒
            if (command.Character.Name == "莫娜" && command.Action == ActionEnum.SwitchLater)
            {
                return 3000;
            }
        }

        return 10000;
    }

    /// <summary>
    /// 获取角色切换顺序
    /// </summary>
    /// <returns></returns>
    public List<int> GetCharacterSwitchOrder()
    {
        List<int> orderList = new List<int>();
        for (var i = 0; i < ActionCommandQueue.Count; i++)
        {
            if (!orderList.Contains(ActionCommandQueue[i].Character.Index))
            {
                orderList.Add(ActionCommandQueue[i].Character.Index);
            }
        }

        return orderList;
    }

    ///// <summary>
    ///// 获取角色存活数量
    ///// </summary>
    ///// <returns></returns>
    //public int GetCharacterAliveNum()
    //{
    //    int num = 0;
    //    foreach (var character in Characters)
    //    {
    //        if (character != null && !character.IsDefeated)
    //        {
    //            num++;
    //        }
    //    }

    //    return num;
    //}


    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width != 1920 || gameScreenSize.Height != 1080)
        {
            _logger.LogWarning("游戏窗口分辨率不是 1920x1080 ！当前分辨率为 {Width}x{Height} , 非 1920x1080 分辨率的游戏可能无法正常使用自动七圣召唤 !", gameScreenSize.Width, gameScreenSize.Height);
        }
    }
}