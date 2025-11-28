using UnityEngine;
using System;

namespace Game.Creature
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
            int rollResult = UnityEngine.Random.Range(1, 21);
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
        public string damageType;

        // Constructor for Dice class
        public Dice(int num, int sides){
            numberOfDice = Math.Max(1, num);
            sidesPerDie = Math.Max(1, sides);
            damageType = string.Empty;
        }
        public Dice(int num, int sides, string dmgType){
            numberOfDice = Math.Max(1, num);
            sidesPerDie = Math.Max(1, sides);
            damageType = dmgType ?? string.Empty;
        }
        // Alt constructor to take string param (accepts "1d6" or "d6")
        public Dice(string diceString){
            if (string.IsNullOrWhiteSpace(diceString))
                throw new ArgumentException("diceString must not be null or empty.", nameof(diceString));

            var s = diceString.Trim().ToLowerInvariant();
            var parts = s.Split('d');
            if (parts.Length != 2)
                throw new ArgumentException("Input must be in the format 'XdY' or 'dY', e.g., '1d6' or 'd6'.");

            var left = parts[0];
            var right = parts[1];

            if (string.IsNullOrEmpty(left))
                left = "1"; // allow "d6" as "1d6"

            if (!int.TryParse(left, out int num) || !int.TryParse(right, out int sides))
                throw new ArgumentException("Input must be numeric in the format 'XdY', e.g., '1d6'.");

            numberOfDice = Math.Max(1, num);
            sidesPerDie = Math.Max(1, sides);
            damageType = string.Empty;
        }
        // Alt constructor specifically for weapon json
        public Dice(int num, string sides, string dmgType){
            numberOfDice = Math.Max(1, num);
            if (string.IsNullOrWhiteSpace(sides))
                throw new ArgumentException(nameof(sides));

            var s = sides.Trim();
            if (s.StartsWith("d", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1);

            if (!int.TryParse(s, out int parsedSides))
                throw new ArgumentException("sides must contain a valid integer, e.g. 'd6' or '6'.");

            sidesPerDie = Math.Max(1, parsedSides);
            damageType = dmgType ?? string.Empty;
        }

        // Roll and return total sum
        public int Roll(){
            int rollsum = 0;
            for (int i = 0; i < numberOfDice; i++){
                rollsum += UnityEngine.Random.Range(1, sidesPerDie + 1);
            }
            return rollsum;
        }

        // Roll and return array of individual die results
        public int[] RollArray(){
            int[] rolls = new int[numberOfDice];
            for (int i = 0; i < numberOfDice; i++){
                rolls[i] = UnityEngine.Random.Range(1, sidesPerDie + 1);
            }
            return rolls;
        }
    }
}