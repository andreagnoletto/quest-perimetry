-- AlterTable
ALTER TABLE "Exam" ADD COLUMN     "normativeSetId" TEXT;

-- AlterTable
ALTER TABLE "Patient" ADD COLUMN     "normativeNoteEnc" TEXT,
ADD COLUMN     "normativeVolunteer" BOOLEAN NOT NULL DEFAULT false;

-- CreateTable
CREATE TABLE "NormativeSet" (
    "id" TEXT NOT NULL,
    "gridId" TEXT NOT NULL,
    "label" TEXT NOT NULL,
    "status" TEXT NOT NULL,
    "statusReason" TEXT,
    "populationEyes" INTEGER NOT NULL,
    "ageMin" DOUBLE PRECISION,
    "ageMax" DOUBLE PRECISION,
    "cohortHash" TEXT NOT NULL,
    "summary" JSONB NOT NULL,
    "database" JSONB NOT NULL,
    "builtAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "builtById" TEXT NOT NULL,
    "activatedAt" TIMESTAMP(3),
    "activatedById" TEXT,

    CONSTRAINT "NormativeSet_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "NormativeSet_gridId_activatedAt_idx" ON "NormativeSet"("gridId", "activatedAt");

-- AddForeignKey
ALTER TABLE "Exam" ADD CONSTRAINT "Exam_normativeSetId_fkey" FOREIGN KEY ("normativeSetId") REFERENCES "NormativeSet"("id") ON DELETE SET NULL ON UPDATE CASCADE;

