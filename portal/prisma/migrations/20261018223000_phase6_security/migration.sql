-- AlterTable
ALTER TABLE "DeviceToken" ADD COLUMN     "expiresAt" TIMESTAMP(3);

-- AlterTable
ALTER TABLE "Patient" ADD COLUMN     "anonymizedAt" TIMESTAMP(3),
ADD COLUMN     "anonymizedById" TEXT,
ADD COLUMN     "consentVersion" TEXT,
ADD COLUMN     "researchConsentAt" TIMESTAMP(3),
ADD COLUMN     "researchConsentById" TEXT;

-- AlterTable
ALTER TABLE "Session" ADD COLUMN     "lastSeenAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- AlterTable
ALTER TABLE "User" ADD COLUMN     "failedLogins" INTEGER NOT NULL DEFAULT 0,
ADD COLUMN     "lockedUntil" TIMESTAMP(3),
ADD COLUMN     "passwordChangedAt" TIMESTAMP(3);

