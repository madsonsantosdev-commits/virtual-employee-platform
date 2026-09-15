# LGPD & Privacy Architecture v1

> Status: Draft v1 — requisito pré-implementação
> Data: 2026-09-15

## 1. Objetivo

Definir os princípios e controles mínimos de privacidade e proteção de dados pessoais da Virtual Employee Platform antes da implementação.

Este documento não substitui revisão jurídica. Ele transforma requisitos de privacidade em decisões arquiteturais e operacionais verificáveis.

## 2. Princípios

A plataforma adota **Privacy by Design** e **Privacy by Default** como requisitos transversais.

Princípios aplicados: finalidade, adequação, necessidade/minimização, transparência, segurança e prevenção, não discriminação, responsabilização e prestação de contas.

Referências oficiais: LGPD — Lei nº 13.709/2018 e orientações da ANPD.

## 3. Papéis no tratamento

Os papéis de Controlador e Operador devem ser definidos por finalidade e contexto. No atendimento do cliente final, o estabelecimento/Tenant tende a atuar como Controlador e a Virtual Employee Platform como Operador nos tratamentos executados por instrução do estabelecimento. A plataforma poderá atuar como Controlador para finalidades próprias, como cadastro e cobrança SaaS, segurança, prevenção de fraude, obrigações legais e gestão contratual.

## 4. Titulares e categorias de dados

### Proprietário / usuário do estabelecimento
Nome, e-mail, telefone, CPF quando pessoa física, autenticação/autorização e dados contratuais/billing quando necessários.

### Profissionais
Nome, contato quando necessário, vínculo com serviços/unidades, disponibilidade e agenda profissional.

### Clientes finais
Nome, telefone/WhatsApp, e-mail opcional, histórico de agendamentos, conteúdo conversacional necessário e identificadores/estado de pagamento retornados pelo gateway.

### Dados fiscais — decisão aprovada
`LegalEntity` pode representar CPF ou CNPJ. CPF é dado pessoal quando relacionado a pessoa natural.

Regras:
- CPF/CNPJ nunca é chave primária; `LegalEntity.Id` é UUID;
- documento é normalizado e validado no backend;
- valor recuperável é armazenado protegido/criptografado;
- comparação e unicidade usam `DocumentFingerprint` via HMAC-SHA-256 com chave secreta da plataforma;
- a unicidade é **tenant-scoped**: `TenantId + DocumentType + DocumentFingerprint`;
- o mesmo CPF/CNPJ pode existir em Tenants diferentes;
- Tenants são independentes e não existe consulta de negócio cross-tenant para informar, relacionar ou bloquear cadastros por documento fiscal;
- um usuário não recebe indicação de que o mesmo documento está cadastrado em outro Tenant;
- documento integral nunca é registrado em logs/traces;
- UI mascara o documento quando a visualização completa não for necessária;
- alterações são auditadas sem replicar o documento integral no Audit Log;
- CPF/CNPJ não é enviado ao LLM sem necessidade explícita e autorizada.

A existência do mesmo documento em Tenants diferentes **não implica identidade de usuário, propriedade compartilhada, vínculo comercial ou compartilhamento de dados**.

## 5. Inventário e finalidade

Antes do go-live deve existir inventário de tratamento com DataCategory, Purpose, DataSubject, ControllerRole, LegalBasisCandidate, Source, Storage, Recipients/Subprocessors, RetentionRule, DeletionOrAnonymizationRule e SecurityClassification.

Consentimento não é base padrão para todo tratamento; a base legal deve ser avaliada por finalidade.

## 6. Minimização de dados na IA

```text
WhatsApp / PWA
   ↓
Conversation Engine
   ↓
Context Builder + Data Minimization
   ↓
AI Gateway
   ↓
LLM Provider
```

O LLM recebe somente o contexto necessário. Não recebe CPF/CNPJ, dados de cartão, secrets ou histórico financeiro completo sem necessidade legítima para a tarefa. LLM não acessa banco diretamente. Logs de prompts/respostas devem minimizar dados pessoais e contexto conversacional terá política de retenção.

## 7. Pagamentos

A plataforma não armazena dados brutos de cartão. Hosted/tokenized checkout envia dados financeiros sensíveis diretamente ao Payment Provider; webhook validado atualiza o backend. A aplicação guarda apenas referências e estados necessários à conciliação.

## 8. Segurança e isolamento

Controles mínimos:
- `TenantId` derivado de contexto confiável;
- autorização server-side;
- Global Query Filters EF Core + validações tenant-aware;
- testes automatizados de isolamento entre tenants;
- nenhum relacionamento implícito entre Tenants por CPF/CNPJ, telefone, e-mail ou outro PII;
- princípio do menor privilégio;
- secrets em secret manager;
- criptografia em trânsito e proteção em repouso;
- mascaramento/redação de PII em logs;
- rate limiting;
- validação de webhooks, idempotência e proteção contra replay;
- audit trail para operações críticas;
- acesso administrativo auditável.

RLS PostgreSQL é evolução possível e não substitui os controles da aplicação.

## 9. Retenção e eliminação

Nenhum dado pessoal deve possuir retenção indefinida apenas por conveniência. Cada categoria terá política considerando finalidade operacional, contrato, obrigação legal/regulatória, exercício de direitos, auditoria e minimização.

Estratégias possíveis: exclusão física, anonimização irreversível, soft-delete quando houver motivo legítimo e retenção diferenciada para Conversations, Webhook payloads e logs.

Pedido de exclusão não implica exclusão automática de tudo; obrigações ou necessidades legítimas de conservação devem ser avaliadas.

## 10. Direitos do titular

O MVP deve permitir processo operacional para confirmação de tratamento, acesso, correção, anonimização/bloqueio/eliminação quando aplicável, informação sobre compartilhamento, revogação de consentimento quando aplicável, oposição/requisições pertinentes e demais direitos aplicáveis. Não é obrigatório um portal completo no MVP, mas deve existir processo rastreável e capacidade técnica de localizar, corrigir, exportar, anonimizar ou eliminar dados quando cabível.

## 11. Solicitações de titulares

Modelo conceitual futuro: `PrivacyRequest` com Id, TenantId quando aplicável, DataSubjectType, RequestType, Status, IdentityVerificationStatus, RequestedAt, DueAt, CompletedAt, Resolution e AuditReference. No MVP pode começar como processo administrativo auditável.

## 12. Compartilhamento e suboperadores

Provedores externos devem ser inventariados por finalidade, categorias enviadas, região/local quando relevante, retenção, segurança, termos/DPA e mecanismo de exclusão/direitos. Grupos previstos: Meta/WhatsApp, LLM provider, gateway de pagamentos, billing SaaS e Azure/infraestrutura/observabilidade. Sempre enviar o mínimo necessário.

## 13. Observabilidade e logs

Preferir IDs internos, TenantId, LocationId, CorrelationId, EventId, códigos de erro e estados técnicos. Evitar CPF/CNPJ integral, cartão, tokens/secrets, payload integral de WhatsApp e prompts/respostas completos por padrão. Conteúdo conversacional armazenado para suporte/auditoria deve ter acesso restrito e retenção definida.

## 14. Incidentes de segurança

Antes do piloto deve existir procedimento para detecção, contenção, preservação de evidências, avaliação de impacto, identificação de titulares/dados afetados, comunicação interna, análise de eventual comunicação ao controlador/titulares/ANPD e registro de decisões e ações corretivas. A decisão jurídica de notificação não deve ser automatizada apenas por regra técnica.

## 15. Requisitos para ERD e código

- entidades tenant-owned carregam `TenantId` quando aplicável;
- `LegalEntity.DocumentEncrypted` protege o valor recuperável;
- `LegalEntity.DocumentFingerprint` permite validação tenant-scoped sem documento em claro;
- não existe UNIQUE global para CPF/CNPJ;
- Location mantém endereço separado da identidade fiscal;
- Customer não usa telefone como PK;
- históricos usam snapshots mínimos necessários;
- APIs não retornam PII desnecessária;
- DTOs públicos devem ser menores que entidades persistidas quando possível;
- endpoints administrativos usam autorização explícita;
- auditoria registra ação, ator, alvo e timestamp sem copiar PII desnecessária.

## 16. Checklist pré-piloto

- [ ] Inventário de dados pessoais aprovado
- [ ] Finalidades e bases legais revisadas
- [ ] Papéis Controlador/Operador documentados por fluxo
- [ ] Política de Privacidade publicada
- [ ] Termos/DPA com assinantes revisados
- [ ] Lista de suboperadores/provedores registrada
- [ ] Retenção definida para Customers, Conversations, Logs, Webhooks e Audit
- [ ] Processo de direitos do titular testado
- [ ] Redação de PII em logs testada
- [ ] Isolamento multi-tenant testado, inclusive documentos fiscais repetidos entre Tenants
- [ ] Backup/restore testado
- [ ] Procedimento de incidente documentado
- [ ] Context Builder/AI Gateway validado para minimização
- [ ] Nenhum dado bruto de cartão trafega pela aplicação

## 17. Decisão central

> **Coletar somente o necessário, usar somente para finalidade definida, compartilhar somente o mínimo, proteger por padrão e manter capacidade de atender os direitos do titular. Cada Tenant permanece uma fronteira independente, mesmo quando dados fiscais coincidem entre contas distintas.**
