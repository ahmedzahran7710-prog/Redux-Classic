-- =====================================================================
-- MoonQueen (Patch 5187) - Game Database Schema for XAMPP / MySQL
-- =====================================================================
CREATE DATABASE IF NOT EXISTS `moonqueen_game` DEFAULT CHARACTER SET utf8 COLLATE utf8_bin;
USE `moonqueen_game`;

DROP TABLE IF EXISTS `character`;
CREATE TABLE `character` (
  `CharacterID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `AccountID` int(10) unsigned NOT NULL,
  `Name` varchar(15) COLLATE utf8_bin NOT NULL,
  `Mesh` int(10) unsigned NOT NULL DEFAULT 1003,
  `Avatar` smallint(5) unsigned NOT NULL DEFAULT 1,
  `Hairstyle` smallint(5) unsigned NOT NULL DEFAULT 535,
  `Silver` int(10) unsigned NOT NULL DEFAULT 10000,
  `Jewels` int(10) unsigned NOT NULL DEFAULT 0,
  `CurrentClass` tinyint(3) unsigned NOT NULL DEFAULT 10,
  `PreviousClass` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `AncestorClass` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `Rebirths` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `Level` tinyint(3) unsigned NOT NULL DEFAULT 1,
  `Experience` bigint(20) unsigned NOT NULL DEFAULT 0,
  `MapID` int(10) unsigned NOT NULL DEFAULT 1002,
  `X` smallint(5) unsigned NOT NULL DEFAULT 430,
  `Y` smallint(5) unsigned NOT NULL DEFAULT 380,
  `Virtue` int(10) unsigned NOT NULL DEFAULT 0,
  `Strength` smallint(5) unsigned NOT NULL DEFAULT 4,
  `Agility` smallint(5) unsigned NOT NULL DEFAULT 6,
  `Vitality` smallint(5) unsigned NOT NULL DEFAULT 12,
  `Spirit` smallint(5) unsigned NOT NULL DEFAULT 0,
  `AttributePoints` smallint(5) unsigned NOT NULL DEFAULT 0,
  `HealthPoints` smallint(5) unsigned NOT NULL DEFAULT 90,
  `ManaPoints` smallint(5) unsigned NOT NULL DEFAULT 0,
  `KillPoints` smallint(5) unsigned NOT NULL DEFAULT 0,
  `EnlightenPoints` smallint(5) unsigned NOT NULL DEFAULT 0,
  `QuizPoints` int(10) unsigned NOT NULL DEFAULT 0,
  `VIPLevel` tinyint(3) unsigned NOT NULL DEFAULT 0,
  `Registered` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`CharacterID`, `AccountID`),
  UNIQUE KEY `CharacterID_UNIQUE` (`CharacterID`),
  UNIQUE KEY `Name_UNIQUE` (`Name`)
) ENGINE=InnoDB AUTO_INCREMENT=1000000 DEFAULT CHARSET=utf8 COLLATE=utf8_bin;
