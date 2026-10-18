-- CreateEnum
CREATE TYPE "Role" AS ENUM ('admin', 'doctor', 'operator');

-- CreateEnum
CREATE TYPE "Integrity" AS ENUM ('verified', 'unverified', 'failed');

-- CreateTable
CREATE TABLE "User" (
    "id" TEXT NOT NULL,
    "email" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "role" "Role" NOT NULL DEFAULT 'doctor',
    "passwordHash" TEXT NOT NULL,
    "active" BOOLEAN NOT NULL DEFAULT true,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "User_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Session" (
    "id" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "expiresAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "Session_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Patient" (
    "id" TEXT NOT NULL,
    "code" TEXT NOT NULL,
    "internalIdEnc" TEXT NOT NULL,
    "internalIdIdx" TEXT NOT NULL,
    "nameEnc" TEXT NOT NULL,
    "nameIdx" TEXT NOT NULL,
    "birthDateEnc" TEXT NOT NULL,
    "sexEnc" TEXT,
    "refractionEnc" TEXT,
    "diagnosisEnc" TEXT,
    "consentAt" TIMESTAMP(3),
    "consentById" TEXT,
    "consentNote" TEXT,
    "researchConsent" BOOLEAN NOT NULL DEFAULT false,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "createdById" TEXT NOT NULL,

    CONSTRAINT "Patient_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Device" (
    "id" TEXT NOT NULL,
    "serial" TEXT NOT NULL,
    "model" TEXT,
    "label" TEXT,
    "hmacKeyEnc" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "Device_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Calibration" (
    "id" TEXT NOT NULL,
    "deviceId" TEXT NOT NULL,
    "measuredAt" TIMESTAMP(3),
    "validUntil" TIMESTAMP(3),
    "photometer" TEXT,
    "json" TEXT NOT NULL,
    "summary" JSONB NOT NULL,
    "uploadedById" TEXT NOT NULL,
    "uploadedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "active" BOOLEAN NOT NULL DEFAULT true,

    CONSTRAINT "Calibration_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ExamOrder" (
    "id" TEXT NOT NULL,
    "patientId" TEXT NOT NULL,
    "presetId" TEXT NOT NULL,
    "ageYears" DOUBLE PRECISION NOT NULL,
    "refraction" TEXT,
    "seed" TEXT,
    "createdById" TEXT NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "fulfilledAt" TIMESTAMP(3),

    CONSTRAINT "ExamOrder_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "HeadsetSession" (
    "id" TEXT NOT NULL,
    "sessionId" TEXT NOT NULL,
    "deviceId" TEXT,
    "startedUtc" TIMESTAMP(3) NOT NULL,
    "outcome" TEXT,
    "rawText" TEXT NOT NULL,
    "data" JSONB NOT NULL,
    "integrity" "Integrity" NOT NULL DEFAULT 'unverified',
    "importedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "HeadsetSession_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Exam" (
    "id" TEXT NOT NULL,
    "examId" TEXT NOT NULL,
    "startedUtc" TIMESTAMP(3) NOT NULL,
    "patientId" TEXT,
    "sessionId" TEXT,
    "eye" TEXT NOT NULL,
    "gridId" TEXT NOT NULL,
    "strategy" TEXT NOT NULL,
    "aborted" BOOLEAN NOT NULL DEFAULT false,
    "configHash" TEXT,
    "calibrated" BOOLEAN NOT NULL DEFAULT false,
    "rawText" TEXT NOT NULL,
    "rawHmac" TEXT,
    "integrity" "Integrity" NOT NULL DEFAULT 'unverified',
    "report" JSONB NOT NULL,
    "analyzedConfig" TEXT NOT NULL,
    "md" DOUBLE PRECISION,
    "vfi" DOUBLE PRECISION,
    "psd" DOUBLE PRECISION,
    "ght" TEXT,
    "importedById" TEXT NOT NULL,
    "importedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "Exam_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "AccessLog" (
    "id" TEXT NOT NULL,
    "at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "userId" TEXT,
    "action" TEXT NOT NULL,
    "entityType" TEXT,
    "entityId" TEXT,
    "detail" TEXT,
    "ip" TEXT,

    CONSTRAINT "AccessLog_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "User_email_key" ON "User"("email");

-- CreateIndex
CREATE INDEX "Session_userId_idx" ON "Session"("userId");

-- CreateIndex
CREATE UNIQUE INDEX "Patient_code_key" ON "Patient"("code");

-- CreateIndex
CREATE UNIQUE INDEX "Patient_internalIdIdx_key" ON "Patient"("internalIdIdx");

-- CreateIndex
CREATE INDEX "Patient_nameIdx_idx" ON "Patient"("nameIdx");

-- CreateIndex
CREATE UNIQUE INDEX "Device_serial_key" ON "Device"("serial");

-- CreateIndex
CREATE UNIQUE INDEX "HeadsetSession_sessionId_key" ON "HeadsetSession"("sessionId");

-- CreateIndex
CREATE INDEX "Exam_patientId_eye_startedUtc_idx" ON "Exam"("patientId", "eye", "startedUtc");

-- CreateIndex
CREATE UNIQUE INDEX "Exam_examId_startedUtc_key" ON "Exam"("examId", "startedUtc");

-- CreateIndex
CREATE INDEX "AccessLog_entityType_entityId_idx" ON "AccessLog"("entityType", "entityId");

-- CreateIndex
CREATE INDEX "AccessLog_at_idx" ON "AccessLog"("at");

-- AddForeignKey
ALTER TABLE "Session" ADD CONSTRAINT "Session_userId_fkey" FOREIGN KEY ("userId") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Calibration" ADD CONSTRAINT "Calibration_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ExamOrder" ADD CONSTRAINT "ExamOrder_patientId_fkey" FOREIGN KEY ("patientId") REFERENCES "Patient"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "HeadsetSession" ADD CONSTRAINT "HeadsetSession_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Exam" ADD CONSTRAINT "Exam_patientId_fkey" FOREIGN KEY ("patientId") REFERENCES "Patient"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Exam" ADD CONSTRAINT "Exam_sessionId_fkey" FOREIGN KEY ("sessionId") REFERENCES "HeadsetSession"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "AccessLog" ADD CONSTRAINT "AccessLog_userId_fkey" FOREIGN KEY ("userId") REFERENCES "User"("id") ON DELETE SET NULL ON UPDATE CASCADE;
