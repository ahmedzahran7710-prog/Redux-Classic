-- =====================================================================
-- MoonQueen (Patch 5187) - Account Database Schema for XAMPP / MySQL
-- =====================================================================
CREATE DATABASE IF NOT EXISTS `moonqueen_account` DEFAULT CHARACTER SET utf8 COLLATE utf8_general_ci;
USE `moonqueen_account`;

SET FOREIGN_KEY_CHECKS = 0;

DROP TABLE IF EXISTS `account_authority`;
CREATE TABLE `account_authority` (
  `AuthorityID` smallint(5) unsigned NOT NULL AUTO_INCREMENT,
  `AuthorityName` varchar(45) NOT NULL,
  PRIMARY KEY (`AuthorityID`),
  UNIQUE KEY `AuthorityID_UNIQUE` (`AuthorityID`),
  UNIQUE KEY `AuthorityName_UNIQUE` (`AuthorityName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

INSERT INTO `account_authority` (`AuthorityID`, `AuthorityName`) VALUES
(1, 'Player'),
(2, 'Moderator'),
(3, 'Game Manager'),
(4, 'Project Manager'),
(5, 'Administrator');

DROP TABLE IF EXISTS `account_status`;
CREATE TABLE `account_status` (
  `StatusID` smallint(5) unsigned NOT NULL AUTO_INCREMENT,
  `StatusName` varchar(45) NOT NULL,
  PRIMARY KEY (`StatusID`),
  UNIQUE KEY `StatusID_UNIQUE` (`StatusID`),
  UNIQUE KEY `StatusName_UNIQUE` (`StatusName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

INSERT INTO `account_status` (`StatusID`, `StatusName`) VALUES
(1, 'Registered'),
(2, 'Activated'),
(3, 'Limited'),
(4, 'Locked'),
(5, 'Banned');

DROP TABLE IF EXISTS `account`;
CREATE TABLE `account` (
  `AccountID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `Username` varchar(16) NOT NULL,
  `Password` varchar(70) NOT NULL,
  `Salt` varchar(45) DEFAULT NULL,
  `AuthorityID` smallint(5) unsigned NOT NULL DEFAULT 1,
  `StatusID` smallint(5) unsigned NOT NULL DEFAULT 2,
  `Name` varchar(70) DEFAULT NULL,
  `Email` varchar(70) DEFAULT NULL,
  `IPAddress` varchar(45) DEFAULT NULL,
  `Registered` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`AccountID`),
  UNIQUE KEY `AccountID_UNIQUE` (`AccountID`),
  UNIQUE KEY `Username_UNIQUE` (`Username`),
  KEY `fk_account_authority_idx` (`AuthorityID`),
  KEY `fk_account_status_idx` (`StatusID`),
  CONSTRAINT `fk_account_authority` FOREIGN KEY (`AuthorityID`) REFERENCES `account_authority` (`AuthorityID`),
  CONSTRAINT `fk_account_status` FOREIGN KEY (`StatusID`) REFERENCES `account_status` (`StatusID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Ready-to-use Administrator account -> Username: admin | Password: 123456
INSERT INTO `account` (`AccountID`, `Username`, `Password`, `Salt`, `AuthorityID`, `StatusID`, `Name`, `IPAddress`)
VALUES (1, 'admin', 'ecda5b3edb0989099b26bda2171982f1d705a675', 'a1b2c3d4e5f60718293a4b5c6d7e8f90', 5, 2, 'MoonQueen Admin', '25.21.212.130');

DROP TRIGGER IF EXISTS `account_passwordhash`;
DELIMITER ;;
CREATE TRIGGER `account_passwordhash` BEFORE INSERT ON `account` FOR EACH ROW
BEGIN
  IF NEW.Salt IS NULL OR NEW.Salt = '' THEN
    SET NEW.Salt = MD5(RAND());
    SET NEW.Password = LOWER(SHA1(CONCAT(NEW.Password, NEW.Salt)));
  END IF;
END;;
DELIMITER ;

DROP TABLE IF EXISTS `logins`;
CREATE TABLE `logins` (
  `Timestamp` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `AccountID` int(10) unsigned NOT NULL,
  `IPAddress` varchar(45) NOT NULL,
  PRIMARY KEY (`AccountID`, `Timestamp`),
  KEY `fk_logins_account_idx` (`AccountID`),
  CONSTRAINT `fk_logins_account` FOREIGN KEY (`AccountID`) REFERENCES `account` (`AccountID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

DROP TABLE IF EXISTS `realm`;
CREATE TABLE `realm` (
  `RealmID` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `Name` varchar(16) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL,
  `AuthorityID` smallint(5) unsigned NOT NULL DEFAULT 1,
  `GameIPAddress` varchar(45) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL DEFAULT '25.21.212.130',
  `RpcIPAddress` varchar(45) CHARACTER SET utf8 COLLATE utf8_bin NOT NULL DEFAULT '127.0.0.1',
  `GamePort` int(10) unsigned NOT NULL DEFAULT 5816,
  `RpcPort` int(10) unsigned NOT NULL DEFAULT 5817,
  PRIMARY KEY (`RealmID`),
  UNIQUE KEY `RealmID_UNIQUE` (`RealmID`),
  UNIQUE KEY `Name_UNIQUE` (`Name`),
  KEY `fk_realm_authority_idx` (`AuthorityID`),
  CONSTRAINT `fk_realm_authority` FOREIGN KEY (`AuthorityID`) REFERENCES `account_authority` (`AuthorityID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COLLATE=utf8_bin;

INSERT INTO `realm` (`RealmID`, `Name`, `AuthorityID`, `GameIPAddress`, `RpcIPAddress`, `GamePort`, `RpcPort`)
VALUES (1, 'MoonQueen', 1, '25.21.212.130', '127.0.0.1', 5816, 5817);

SET FOREIGN_KEY_CHECKS = 1;


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
