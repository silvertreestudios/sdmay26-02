
using UnityEngine;
using System.Collections.Generic;
using Game.Damage;

namespace Game.Dice
{
    // Degrees of success/failure for d20 rolls
    public enum DegreeOfSuccess{
        CriticalFail,
        Fail,
        Success,
        CriticalSuccess
    }

    // Struct for d20 roll results
    public struct D20Result{
        public int roll;
        public int total;
        public DegreeOfSuccess degree;
    }

    // Class for rolling d20 with modifiers against target value
    public class D20{
        public static D20Result Roll(int modifier, int targetVal){
            int rollResult = Random.Range(1, 21);
            int totalResult = rollResult + modifier;
            DegreeOfSuccess degree;
            if (rollResult == 20 || totalResult >= targetVal + 10){
                degree = DegreeOfSuccess.CriticalSuccess;
            }
            else if (rollResult == 1 || totalResult <= targetVal - 10){
                degree = DegreeOfSuccess.CriticalFail;
            }
            else if (totalResult >= targetVal){
                degree = DegreeOfSuccess.Success;
            }
            else{
                degree = DegreeOfSuccess.Fail;
            }
            return new D20Result{ roll = rollResult, total = totalResult, degree = degree };
        }
    }

    // Class for rolling dice of given number and sides
    public class Dice{
        public int numberOfDice;
        public int sidesPerDie;
        public DamageType damageType;

        // Constructor for Dice class
        public Dice(int num, int sides){
            numberOfDice = num;
            sidesPerDie = sides;
            damageType = null;
        }
        public Dice(int num, int sides, DamageType dmgType){
            numberOfDice = num;
            sidesPerDie = sides;
            damageType = dmgType;
        }

        // Alt constructor to take string param 
        public Dice(string diceString){
            var parts = diceString.ToLower().Split('d');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int num) || !int.TryParse(parts[1], out int sides))
                throw new ArgumentException("Input must be in the format 'XdY', e.g., '1d6'.");
            numberOfDice = num;
            sidesPerDie = sides;
            damageType = null;
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