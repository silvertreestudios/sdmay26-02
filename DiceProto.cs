
using UnityEngine;
using System.Collections.Generic;

namespace Game.Dice
{
    // Degrees of success/failure for d20 rolls
    public enum D20Status{
        CriticalFail,
        Fail,
        Success,
        CriticalSuccess
    }

    // Struct for d20 roll results
    public struct D20Result{
        public int roll;
        public int total;
        public D20Status status;
    }

    // Class for rolling d20 with modifiers against target value
    public class Roll20{
        public static D20Result RollD20(int modifier, int targetVal){
            int rollResult = Random.Range(1, 21);
            int totalResult = rollResult + modifier;
            D20Status status;
            if (rollResult == 20 || totalResult >= targetVal + 10){
                status = D20Status.CriticalSuccess;
            }
            else if (rollResult == 1 || totalResult <= targetVal - 10){
                status = D20Status.CriticalFail;
            }
            else if (totalResult >= targetVal){
                status = D20Status.Success;
            }
            else{
                status = D20Status.Fail;
            }
            return new D20Result{ roll = rollResult, total = totalResult, status = status };
        }
    }

    // Class for rolling dice of given number and sides
    public class Dice{
        public int numberOfDice;
        public int sidesPerDie;

        // Constructor for Dice class
        public Dice(int num, int sides){
            numberOfDice = num;
            sidesPerDie = sides;
        }

        // Roll and return total sum
        public int Roll(){
            int rollsum = 0;
            for (int i = 0; i < numberOfDice; i++){
                rollsum += Random.Range(1, sidesPerDie + 1);
            }
            return rollsum;
        }

        // Roll and return array of individual die results
        public int[] RollArray(){
            int[] rolls = new int[numberOfDice];
            for (int i = 0; i < numberOfDice; i++){
                rolls[i] = Random.Range(1, sidesPerDie + 1);
            }
            return rolls;
        }
    }
}